namespace Notifications.Application.Routing;

public class RouteDefinition
{
    public required string RouteKey { get; set; }
    public required string DisplayName { get; set; }
    public required string TargetScreen { get; set; }
    public List<string> RequiredParameters { get; set; } = new();
    public List<string> OptionalParameters { get; set; } = new();
    public bool RequiresAuth { get; set; } = true;
    public string? Description { get; set; }
}

public static class RouteRegistry
{
    private static readonly Dictionary<string, RouteDefinition> _routes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["general.home"] = new()
        {
            RouteKey = "general.home",
            DisplayName = "Home Dashboard",
            TargetScreen = "/dashboard",
            Description = "Main student dashboard home tab"
        },
        ["academic.assignments"] = new()
        {
            RouteKey = "academic.assignments",
            DisplayName = "Assignments Overview",
            TargetScreen = "/academics/assignments",
            OptionalParameters = new() { "courseOfferingId" },
            Description = "List of assignments for current semester"
        },
        ["assignment.details"] = new()
        {
            RouteKey = "assignment.details",
            DisplayName = "Assignment Details",
            TargetScreen = "/academics/assignments/details",
            RequiredParameters = new() { "assignmentId" },
            OptionalParameters = new() { "courseOfferingId" },
            Description = "Specific assignment detail and submission sheet"
        },
        ["academic.attendance"] = new()
        {
            RouteKey = "academic.attendance",
            DisplayName = "Attendance Overview",
            TargetScreen = "/academics/attendance",
            OptionalParameters = new() { "courseOfferingId", "academicTermId" },
            Description = "Cumulative student attendance breakdown"
        },
        ["attendance.details"] = new()
        {
            RouteKey = "attendance.details",
            DisplayName = "Attendance Session Details",
            TargetScreen = "/academics/attendance/details",
            RequiredParameters = new() { "attendanceSessionId" },
            Description = "Individual lecture attendance mark"
        },
        ["academic.internals"] = new()
        {
            RouteKey = "academic.internals",
            DisplayName = "Internal Assessments Overview",
            TargetScreen = "/academics/internals",
            OptionalParameters = new() { "courseOfferingId" },
            Description = "Internal test scores and marks"
        },
        ["internal_assessment.details"] = new()
        {
            RouteKey = "internal_assessment.details",
            DisplayName = "Assessment Component Details",
            TargetScreen = "/academics/internals/details",
            RequiredParameters = new() { "assessmentId" },
            Description = "Individual test score breakdown"
        },
        ["academic.results"] = new()
        {
            RouteKey = "academic.results",
            DisplayName = "Results & GPA Overview",
            TargetScreen = "/academics/results",
            OptionalParameters = new() { "academicTermId" },
            Description = "Semester grades and cumulative GPA"
        },
        ["results.details"] = new()
        {
            RouteKey = "results.details",
            DisplayName = "Semester Result Slip",
            TargetScreen = "/academics/results/details",
            RequiredParameters = new() { "resultId" },
            Description = "Detailed grade card breakdown"
        },
        ["academic.timetable"] = new()
        {
            RouteKey = "academic.timetable",
            DisplayName = "Class Timetable",
            TargetScreen = "/academics/timetable",
            OptionalParameters = new() { "academicTermId" },
            Description = "Weekly class schedule"
        },
        ["announcement.details"] = new()
        {
            RouteKey = "announcement.details",
            DisplayName = "VisionX Announcement",
            TargetScreen = "/announcements/details",
            RequiredParameters = new() { "announcementId" },
            Description = "Official notice or circular"
        },
        ["club.details"] = new()
        {
            RouteKey = "club.details",
            DisplayName = "Club Details",
            TargetScreen = "/clubs/details",
            RequiredParameters = new() { "clubId" },
            Description = "Club information and events"
        },
        ["clubs.explore"] = new()
        {
            RouteKey = "clubs.explore",
            DisplayName = "Explore Clubs",
            TargetScreen = "/clubs/explore",
            Description = "Browse campus societies and clubs"
        },
        ["settings.notifications"] = new()
        {
            RouteKey = "settings.notifications",
            DisplayName = "Notification Settings",
            TargetScreen = "/settings/notifications",
            Description = "Manage notification category preferences"
        },
        ["alerts.inbox"] = new()
        {
            RouteKey = "alerts.inbox",
            DisplayName = "Alerts & Notifications Inbox",
            TargetScreen = "/notifications",
            Description = "Notification inbox and detection alerts history"
        },
        ["devices.list"] = new()
        {
            RouteKey = "devices.list",
            DisplayName = "Connected Devices",
            TargetScreen = "/devices",
            Description = "VisionX cameras and connected edge hardware"
        },
        ["camera.capture"] = new()
        {
            RouteKey = "camera.capture",
            DisplayName = "Camera Stream & Capture",
            TargetScreen = "/capture",
            Description = "Live camera view and capture inspection"
        },
        ["settings.profile"] = new()
        {
            RouteKey = "settings.profile",
            DisplayName = "Profile & Settings",
            TargetScreen = "/more",
            Description = "User profile and system settings"
        },
        ["alert.details"] = new()
        {
            RouteKey = "alert.details",
            DisplayName = "Alert Details",
            TargetScreen = "/notifications/details",
            RequiredParameters = new() { "notificationId" },
            Description = "Detailed detection alert and frame snapshot"
        }
    };

    public static IReadOnlyCollection<RouteDefinition> GetAllRoutes() => _routes.Values;

    public static bool TryGetRoute(string? routeKey, out RouteDefinition route)
    {
        if (string.IsNullOrWhiteSpace(routeKey))
        {
            route = null!;
            return false;
        }
        return _routes.TryGetValue(routeKey, out route!);
    }

    public static (bool IsValid, string? ErrorMessage) ValidateRoute(string? routeKey, Dictionary<string, string>? parameters)
    {
        var (isValid, error) = ValidateRouteAndParameters(routeKey, parameters);
        return (isValid, error);
    }

    public static (bool IsValid, string? Error) ValidateRouteAndParameters(string? routeKey, Dictionary<string, string>? parameters)
    {
        if (string.IsNullOrWhiteSpace(routeKey))
        {
            return (true, null); // Optional destination
        }

        if (!_routes.TryGetValue(routeKey, out var def))
        {
            return (false, $"Unknown route key '{routeKey}'. Supported routes: {string.Join(", ", _routes.Keys)}");
        }

        if (def.RequiredParameters.Count > 0)
        {
            parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var req in def.RequiredParameters)
            {
                if (!parameters.TryGetValue(req, out var val) || string.IsNullOrWhiteSpace(val))
                {
                    return (false, $"Route '{routeKey}' requires parameter '{req}'.");
                }
            }
        }

        return (true, null);
    }
}
