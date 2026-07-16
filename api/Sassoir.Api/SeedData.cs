using Sassoir.Api.Models;

namespace Sassoir.Api.Data;

public static class SeedData
{
    public static readonly List<EventDetails> Events =
    [
        new(
            Guid.Parse("2eb2f4b0-67c8-4d99-a91f-caa1007084e8"),
            "Lichaa & Roula's Wedding",
            "lichaa-and-roula",
            "Wedding",
            "Together with their families, they welcome you to an evening of love, dinner, and dancing.",
            "Saturday, August 22",
            "The Olive Garden Venue",
            "Beirut, Lebanon",
            EventStatus.Published,
            new EventTheme(
                "L & R",
                "An elegant garden celebration under soft summer lights.",
                "#D8CFBC",
                "#565449",
                "#FFFBF4",
                "#11120D",
                "Welcome to Licha & Roula's wedding",
                "Search by name",
                "Search by name",
                "/guest-wedding-banner.png"),
            new FloorPlanDto(
                "Garden Ballroom",
                1.14m,
                [
                    new("stage", "stage", "Stage", null, null, 0.35m, 0.06m, 0.38m, 0.11m, "rect", 1),
                    new("table-8", "table", "Table 8", null, "8", 0.13m, 0.25m, 0.15m, 0.15m, "round", 2),
                    new("table-10", "table", "Table 10", null, "10", 0.13m, 0.53m, 0.16m, 0.16m, "round", 2),
                    new("dance", "dance", "Dance Floor", null, null, 0.42m, 0.40m, 0.28m, 0.25m, "rect", 1),
                    new("bar", "bar", "Bar", null, null, 0.82m, 0.27m, 0.13m, 0.25m, "rect", 1),
                    new("table-12", "table", "Table 12", null, "12", 0.76m, 0.56m, 0.15m, 0.15m, "round", 2),
                    new("restroom", "restroom", "Toilets", null, null, 0.83m, 0.69m, 0.13m, 0.12m, "rect", 1),
                    new("table-14", "table", "Table 14", null, "14", 0.75m, 0.82m, 0.16m, 0.16m, "round", 2),
                    new("entrance", "entrance", "Entrance", null, null, 0.10m, 0.83m, 0.15m, 0.09m, "rect", 1)
                ]),
            [
                new(Guid.Parse("29a84b1f-0ae4-4f31-9df6-0918f26f3d78"), "guest-sarah-lichaa", "Sarah Lichaa", "Lichaa Family", "12", "The Olive Garden", "4", "Near the dance floor, with a clear view of the stage.", GuestStatus.Active, ["sarah", "sara lichaa", "Ø³Ø§Ø±Ø© Ù„Ø­Ø§Ø¡"], ["Roula L.", "Maya K.", "Karim H."]),
                new(Guid.Parse("c67681f8-82e6-4142-b204-64e26a0e63e4"), "guest-roula-lichaa", "Roula Lichaa", "Couple's Table", "12", "The Olive Garden", "1", "Near the dance floor, with a clear view of the stage.", GuestStatus.Active, ["roula", "rula", "Ø±ÙˆÙ„Ø§"], ["Sarah L.", "Maya K.", "Karim H."]),
                new(Guid.Parse("a4f451b7-37d8-498d-926c-6a5b8ffbbbd7"), "guest-maya-k", "Maya K.", "Friends of Roula", "12", "The Olive Garden", "5", "Near the dance floor, with a clear view of the stage.", GuestStatus.Active, ["maya", "maia", "Ù…Ø§ÙŠØ§"], ["Sarah L.", "Roula L.", "Karim H."]),
                new(Guid.Parse("7816fa19-2877-4de8-bdde-769739e5f9e9"), "guest-antonella-hitti", "Antonella Hitti", "Hitti Family", "8", "Cedar Grove", "2", "Close to the garden entrance.", GuestStatus.Active, ["antonella", "antoinella", "hitti", "Ø§Ù†Ø·ÙˆÙ†ÙŠÙ„Ø§"], ["Nadine H.", "Marc H."]),
                new(Guid.Parse("6904d89b-6182-4dbb-9b4c-3e7aa1ec2ff7"), "guest-antonella-h", "Antonella H.", "Guest of Roula", "10", "Jasmine Court", null, "Beside the left garden aisle.", GuestStatus.Active, ["antonella guest of roula", "anto"], ["Lea R.", "Nour S."]),
                new(Guid.Parse("16f35526-7734-4794-8ad1-f78db0874368"), "guest-karim-h", "Karim Haddad", "Friends of Lichaa", "14", "Terrace", null, "Near the lower terrace aisle.", GuestStatus.Active, ["karim", "ÙƒØ±ÙŠÙ…"], ["Omar D.", "Elias B."])
            ])
    ];
}

