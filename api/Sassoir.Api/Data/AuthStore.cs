using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Sassoir.Api.Models;

namespace Sassoir.Api.Data
{
    public sealed class AuthOptions
    {
        public string Issuer { get; set; } = "sassoir.local";
        public string Audience { get; set; } = "sassoir.admin";
        public string SigningKey { get; set; } = string.Empty;
        public int AccessTokenMinutes { get; set; } = 120;
        public int RefreshTokenHours { get; set; } = 24;
        public int PasswordResetTokenMinutes { get; set; } = 30;
        public string SeedAdminEmail { get; set; } = "admin@sassoir.com";
        public string SeedAdminPassword { get; set; } = string.Empty;
    }

    public sealed class AuthStore
    {
        private readonly SassoirDbContext _db;
        private readonly AuthOptions _options;

        public AuthStore(SassoirDbContext db, IOptions<AuthOptions> options)
        {
            _db = db;
            _options = options.Value;
            EnsureAuthTables();
            EnsureSeedAdmin();
        }

        public LoginResponse? Login(string email, string password)
        {
            var normalizedEmail = email.Trim().ToLowerInvariant();
            var user = _db.Users
                .Include(item => item.UserRoles)
                    .ThenInclude(item => item.Role)
                .SingleOrDefault(item => item.Email.ToLower() == normalizedEmail && item.Status == "Active");

            if (user is null || !PasswordHasher.Verify(password, user.PasswordHash))
            {
                return null;
            }

            user.LastLoginAt = DateTimeOffset.UtcNow;
            _db.SaveChanges();

            var roles = user.UserRoles.Select(item => item.Role?.Name).Where(item => item is not null).Cast<string>().ToArray();
            return new LoginResponse(
                TokenSigner.Create(user, roles, _options),
                TokenSigner.CreateRefresh(user, _options),
                user.Email,
                $"{user.FirstName} {user.LastName}".Trim(),
                roles,
                DateTimeOffset.UtcNow.AddMinutes(_options.AccessTokenMinutes),
                DateTimeOffset.UtcNow.AddHours(_options.RefreshTokenHours));
        }

        public LoginResponse? Refresh(string refreshToken)
        {
            var claims = TokenSigner.Validate(refreshToken, _options, "refresh");
            if (claims is null) return null;

            var user = _db.Users
                .Include(item => item.UserRoles)
                    .ThenInclude(item => item.Role)
                .SingleOrDefault(item => item.Id == claims.UserId && item.Status == "Active");
            if (user is null) return null;

            var roles = user.UserRoles.Select(item => item.Role?.Name).Where(item => item is not null).Cast<string>().ToArray();
            return new LoginResponse(
                TokenSigner.Create(user, roles, _options),
                TokenSigner.CreateRefresh(user, _options),
                user.Email,
                $"{user.FirstName} {user.LastName}".Trim(),
                roles,
                DateTimeOffset.UtcNow.AddMinutes(_options.AccessTokenMinutes),
                DateTimeOffset.UtcNow.AddHours(_options.RefreshTokenHours));
        }

        public CurrentUserDto? GetCurrentUser(HttpRequest request)
        {
            var claims = ValidateRequest(request);
            return claims is null
                ? null
                : new CurrentUserDto(claims.Email, claims.DisplayName, claims.Roles);
        }

        public bool IsAdmin(HttpRequest request)
        {
            var claims = ValidateRequest(request);
            return claims?.Roles.Any(role => role is "Admin" or "SuperAdmin") == true;
        }

        public string? ChangePassword(HttpRequest request, string currentPassword, string newPassword)
        {
            var claims = ValidateRequest(request);
            if (claims is null) return "unauthorized";
            if (newPassword.Length < 8) return "New password must be at least 8 characters.";

            var user = _db.Users.SingleOrDefault(item => item.Id == claims.UserId && item.Status == "Active");
            if (user is null) return "unauthorized";
            if (!PasswordHasher.Verify(currentPassword, user.PasswordHash)) return "Current password is incorrect.";

            user.PasswordHash = PasswordHasher.Hash(newPassword);
            user.UpdatedAt = DateTimeOffset.UtcNow;
            _db.SaveChanges();
            return null;
        }

        public string? CreatePasswordReset(string email)
        {
            var normalizedEmail = email.Trim().ToLowerInvariant();
            var user = _db.Users.SingleOrDefault(item => item.Email.ToLower() == normalizedEmail && item.Status == "Active");
            return user is null ? null : TokenSigner.CreatePasswordReset(user, _options);
        }

        public string? ResetPassword(string resetToken, string newPassword)
        {
            if (newPassword.Length < 8) return "New password must be at least 8 characters.";

            var claims = TokenSigner.Validate(resetToken, _options, "password-reset");
            if (claims is null) return "unauthorized";

            var user = _db.Users.SingleOrDefault(item => item.Id == claims.UserId && item.Status == "Active");
            if (user is null) return "unauthorized";

            user.PasswordHash = PasswordHasher.Hash(newPassword);
            user.UpdatedAt = DateTimeOffset.UtcNow;
            _db.SaveChanges();
            return null;
        }

        private TokenClaims? ValidateRequest(HttpRequest request)
        {
            var authorization = request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;

            return TokenSigner.Validate(authorization["Bearer ".Length..].Trim(), _options, "access");
        }

        private void EnsureAuthTables()
        {
            _db.Database.ExecuteSqlRaw("""
                create table if not exists app_users (
                  id uuid primary key,
                  organization_id uuid null references organizations(id) on delete set null,
                  first_name text not null,
                  last_name text not null,
                  email text not null unique,
                  password_hash text not null,
                  status text not null default 'Active',
                  is_super_admin boolean not null default false,
                  last_login_at timestamptz null,
                  created_at timestamptz not null default now(),
                  updated_at timestamptz not null default now()
                );

                create table if not exists roles (
                  id uuid primary key,
                  name text not null unique
                );

                create table if not exists user_roles (
                  user_id uuid not null references app_users(id) on delete cascade,
                  role_id uuid not null references roles(id) on delete cascade,
                  primary key (user_id, role_id)
                );
            """);
        }

        private void EnsureSeedAdmin()
        {
            if (string.IsNullOrWhiteSpace(_options.SeedAdminEmail) || string.IsNullOrWhiteSpace(_options.SeedAdminPassword))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var adminRole = _db.Roles.SingleOrDefault(item => item.Name == "Admin");
            if (adminRole is null)
            {
                adminRole = new RoleEntity { Id = Guid.NewGuid(), Name = "Admin" };
                _db.Roles.Add(adminRole);
                _db.SaveChanges();
            }

            var normalizedEmail = _options.SeedAdminEmail.Trim().ToLowerInvariant();
            var user = _db.Users
                .Include(item => item.UserRoles)
                .SingleOrDefault(item => item.Email.ToLower() == normalizedEmail);

            if (user is null)
            {
                user = new AppUserEntity
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Sassoir",
                    LastName = "Admin",
                    Email = normalizedEmail,
                    PasswordHash = PasswordHasher.Hash(_options.SeedAdminPassword),
                    Status = "Active",
                    IsSuperAdmin = true,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _db.Users.Add(user);
                _db.SaveChanges();
            }

            if (!_db.UserRoles.Any(item => item.UserId == user.Id && item.RoleId == adminRole.Id))
            {
                _db.UserRoles.Add(new UserRoleEntity { UserId = user.Id, RoleId = adminRole.Id });
                _db.SaveChanges();
            }
        }
    }

    public sealed record TokenClaims(Guid UserId, string Email, string DisplayName, string[] Roles);

    public static class TokenSigner
    {
        public static string Create(AppUserEntity user, string[] roles, AuthOptions options)
        {
            return Create(user, roles, options, "access", DateTimeOffset.UtcNow.AddMinutes(options.AccessTokenMinutes));
        }

        public static string CreateRefresh(AppUserEntity user, AuthOptions options)
        {
            return Create(user, [], options, "refresh", DateTimeOffset.UtcNow.AddHours(options.RefreshTokenHours));
        }

        public static string CreatePasswordReset(AppUserEntity user, AuthOptions options)
        {
            return Create(user, [], options, "password-reset", DateTimeOffset.UtcNow.AddMinutes(options.PasswordResetTokenMinutes));
        }

        private static string Create(AppUserEntity user, string[] roles, AuthOptions options, string tokenType, DateTimeOffset expiresAt)
        {
            var now = DateTimeOffset.UtcNow;
            var payload = new Dictionary<string, object?>
            {
                ["iss"] = options.Issuer,
                ["aud"] = options.Audience,
                ["sub"] = user.Id.ToString(),
                ["email"] = user.Email,
                ["name"] = $"{user.FirstName} {user.LastName}".Trim(),
                ["roles"] = roles,
                ["typ"] = tokenType,
                ["iat"] = now.ToUnixTimeSeconds(),
                ["exp"] = expiresAt.ToUnixTimeSeconds()
            };

            var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
            var body = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
            var unsigned = $"{header}.{body}";
            var signature = Sign(unsigned, options.SigningKey);
            return $"{unsigned}.{signature}";
        }

        public static TokenClaims? Validate(string token, AuthOptions options, string expectedTokenType)
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return null;

            var unsigned = $"{parts[0]}.{parts[1]}";
            var expected = Sign(unsigned, options.SigningKey);
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[2])))
            {
                return null;
            }

            using var document = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            var root = document.RootElement;
            if (root.GetProperty("iss").GetString() != options.Issuer) return null;
            if (root.GetProperty("aud").GetString() != options.Audience) return null;
            if (root.GetProperty("exp").GetInt64() <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;
            if (root.TryGetProperty("typ", out var tokenType) && tokenType.GetString() != expectedTokenType) return null;
            if (!root.TryGetProperty("typ", out _) && expectedTokenType != "access") return null;
            if (!Guid.TryParse(root.GetProperty("sub").GetString(), out var userId)) return null;

            var roles = root.GetProperty("roles").EnumerateArray().Select(item => item.GetString()).Where(item => item is not null).Cast<string>().ToArray();
            return new TokenClaims(
                userId,
                root.GetProperty("email").GetString() ?? string.Empty,
                root.GetProperty("name").GetString() ?? string.Empty,
                roles);
        }

        private static string Sign(string value, string signingKey)
        {
            if (signingKey.Length < 32)
            {
                throw new InvalidOperationException("Auth signing key must be at least 32 characters.");
            }

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
            return Base64Url(hmac.ComputeHash(Encoding.ASCII.GetBytes(value)));
        }

        private static string Base64Url(byte[] value)
        {
            return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static byte[] Base64UrlDecode(string value)
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            return Convert.FromBase64String(padded);
        }
    }

    public static class PasswordHasher
    {
        private const int SaltSize = 16;
        private const int KeySize = 32;
        private const int Iterations = 100_000;

        public static string Hash(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
            return $"pbkdf2-sha256.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
        }

        public static bool Verify(string password, string storedHash)
        {
            var parts = storedHash.Split('.');
            if (parts.Length != 4 || parts[0] != "pbkdf2-sha256") return false;
            if (!int.TryParse(parts[1], out var iterations)) return false;

            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
    }
}
