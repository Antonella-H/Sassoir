using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Sassoir.Api.Data;
using Sassoir.Api.Models;

namespace Sassoir.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", (LoginRequest request, AuthStore auth) =>
        {
            var login = auth.Login(request.Email, request.Password);
            return login is null ? Results.Unauthorized() : Results.Ok(login);
        });

        app.MapPost("/api/auth/refresh", (RefreshTokenRequest request, AuthStore auth) =>
        {
            var login = auth.Refresh(request.RefreshToken);
            return login is null ? Results.Unauthorized() : Results.Ok(login);
        });

        app.MapGet("/api/auth/me", (HttpRequest request, AuthStore auth) =>
        {
            var user = auth.GetCurrentUser(request);
            return user is null ? Results.Unauthorized() : Results.Ok(user);
        });

        app.MapPost("/api/auth/change-password", (ChangePasswordRequest password, HttpRequest request, AuthStore auth) =>
        {
            var result = auth.ChangePassword(request, password.CurrentPassword, password.NewPassword);
            return result switch
            {
                "unauthorized" => Results.Unauthorized(),
                not null => Results.BadRequest(new { message = result }),
                _ => Results.Ok(new { status = "updated" })
            };
        });

        app.MapPost("/api/auth/forgot-password", (ForgotPasswordRequest password, AuthStore auth) =>
        {
            var reset = auth.CreatePasswordReset(password.Email);
            return Results.Ok(new
            {
                message = "If the email belongs to an admin account, a reset link can be sent.",
                resetToken = reset
            });
        });

        app.MapPost("/api/auth/reset-password", (ResetPasswordRequest password, AuthStore auth) =>
        {
            var result = auth.ResetPassword(password.ResetToken, password.NewPassword);
            return result switch
            {
                "unauthorized" => Results.Unauthorized(),
                not null => Results.BadRequest(new { message = result }),
                _ => Results.Ok(new { status = "updated" })
            };
        });

        return app;
    }

}