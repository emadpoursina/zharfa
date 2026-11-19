namespace Zharfa.Api.Tests.Platform;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zharfa.Api.Analytics;
using Zharfa.Api.Controllers;
using Zharfa.Api.Platform;
using Zharfa.Api.Security;

/// <summary>
/// T067 (spec 003 US6, FR-021..FR-023, contracts/api.md §5–§6): the <c>[DemoOnly]</c> filter decision,
/// the <c>modules</c> flags by mode and role, and which controllers carry the attribute.
/// Pure: no database, no network, no HTTP server.
/// </summary>
public class DemoOnlyFilterTests
{
    private static readonly DataModeOptions Production = new("production");
    private static readonly DataModeOptions Demo = new("demo");

    /// <summary>Runs the filter with a real <see cref="DataModeOptions"/> in the request services.</summary>
    private static async Task<(ActionExecutingContext Context, int HandlerCalls)> RunFilterAsync(DataModeOptions mode)
    {
        var services = new ServiceCollection();
        services.AddSingleton(mode);

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: new object());

        var handlerCalls = 0;
        Task<ActionExecutedContext> Handler()
        {
            handlerCalls++;
            var executed = new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), controller: new object());
            return Task.FromResult(executed);
        }

        await new DemoOnlyAttribute().OnActionExecutionAsync(context, Handler);
        return (context, handlerCalls);
    }

    // ---- Filter decision (production refuses, demo passes through) ----

    [Fact]
    public void Production_refuses_demo_only_modules()
    {
        Assert.True(DemoOnlyRules.IsRefused(Production));
        Assert.False(DemoOnlyRules.IsRefused(Demo));
    }

    [Fact]
    public async Task Production_returns_404_with_the_persian_envelope_before_the_handler_runs()
    {
        var (context, handlerCalls) = await RunFilterAsync(Production);

        var result = Assert.IsType<NotFoundObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);

        var message = result.Value!.GetType().GetProperty("message")!.GetValue(result.Value);
        Assert.Equal("این بخش در این نسخه فعال نیست.", message);

        Assert.Equal(0, handlerCalls);
    }

    [Fact]
    public async Task Demo_passes_the_request_through_to_the_handler()
    {
        var (context, handlerCalls) = await RunFilterAsync(Demo);

        Assert.Null(context.Result);
        Assert.Equal(1, handlerCalls);
    }

    [Fact]
    public void Filter_runs_before_the_framework_invalid_model_state_filter()
    {
        // The framework's invalid-model-state filter has order -2000; a lower order runs first, so a
        // production request is answered 404 even when its body is malformed.
        var order = ((IOrderedFilter)new DemoOnlyAttribute()).Order;

        Assert.True(order < -2000, $"DemoOnly order {order} must run before the model-state filter (-2000).");
    }

    // ---- modules flags by mode and permission set (contracts/api.md §7) ----

    [Fact]
    public void Production_admin_sees_admin_modules_without_demo_pages_or_ai()
    {
        var modules = SystemModules.For(Production, RolePermissions.For(Roles.Admin));

        Assert.True(modules.Overview);
        Assert.True(modules.PurchasingReports);
        Assert.True(modules.SavedReports);
        Assert.True(modules.DataSync);
        Assert.True(modules.Users);
        Assert.True(modules.AuditLog);
        Assert.True(modules.DataQuality);
        Assert.True(modules.AiSettings);
        Assert.False(modules.DemoPages);
        Assert.False(modules.Ai);
    }

    [Fact]
    public void Production_purchasing_has_no_admin_modules_and_has_ai()
    {
        var modules = SystemModules.For(Production, RolePermissions.For(Roles.Purchasing));

        Assert.True(modules.Overview);
        Assert.True(modules.PurchasingReports);
        Assert.True(modules.SavedReports);
        Assert.True(modules.Ai);
        Assert.False(modules.DataSync);
        Assert.False(modules.Users);
        Assert.False(modules.AuditLog);
        Assert.False(modules.DataQuality);
        Assert.False(modules.AiSettings);
        Assert.False(modules.DemoPages);
    }

    [Fact]
    public void Production_management_has_ai_and_no_admin_modules()
    {
        var modules = SystemModules.For(Production, RolePermissions.For(Roles.Management));

        Assert.True(modules.Ai);
        Assert.True(modules.PurchasingReports);
        Assert.True(modules.SavedReports);
        Assert.False(modules.AiSettings);
        Assert.False(modules.DemoPages);
        Assert.False(modules.DataSync);
        Assert.False(modules.Users);
        Assert.False(modules.AuditLog);
        Assert.False(modules.DataQuality);
    }

    [Fact]
    public void Demo_admin_has_demo_pages_but_no_ai_because_admin_lacks_ai_query()
    {
        var modules = SystemModules.For(Demo, RolePermissions.For(Roles.Admin));

        Assert.True(modules.DemoPages);
        Assert.True(modules.SavedReports);
        Assert.False(modules.Ai);
        Assert.True(modules.DataSync);
        Assert.True(modules.AiSettings);
    }

    [Fact]
    public void Demo_management_has_ai_and_no_admin_modules()
    {
        var modules = SystemModules.For(Demo, RolePermissions.For(Roles.Management));

        Assert.True(modules.Ai);
        Assert.True(modules.PurchasingReports);
        Assert.True(modules.SavedReports);
        Assert.True(modules.DemoPages);
        Assert.False(modules.DataSync);
        Assert.False(modules.Users);
        Assert.False(modules.AuditLog);
        Assert.False(modules.DataQuality);
        Assert.False(modules.AiSettings);
    }

    [Fact]
    public void Empty_permission_set_turns_every_module_off()
    {
        var modules = SystemModules.For(Demo, RolePermissions.For(null));

        Assert.False(modules.Overview);
        Assert.False(modules.DataSync);
        Assert.False(modules.DemoPages);
        Assert.False(modules.Ai);
        Assert.False(modules.PurchasingReports);
        Assert.False(modules.SavedReports);
        Assert.False(modules.Users);
        Assert.False(modules.AuditLog);
        Assert.False(modules.DataQuality);
        Assert.False(modules.AiSettings);
    }

    [Fact]
    public void SavedReports_module_is_true_iff_purchasing_reports_permission_is_granted()
    {
        var adminModules = SystemModules.For(Production, RolePermissions.For(Roles.Admin));
        var purchasingModules = SystemModules.For(Production, RolePermissions.For(Roles.Purchasing));
        var financeModules = SystemModules.For(Production, RolePermissions.For(Roles.Finance));
        var emptyModules = SystemModules.For(Production, new HashSet<string>());
        var demoAdminModules = SystemModules.For(Demo, RolePermissions.For(Roles.Admin));

        Assert.True(adminModules.SavedReports);
        Assert.True(purchasingModules.SavedReports);
        Assert.True(financeModules.SavedReports);
        Assert.False(emptyModules.SavedReports);
        Assert.True(demoAdminModules.SavedReports);
    }

    // ---- AI payload labels (FR-022, contracts/api.md §5) ----

    [Fact]
    public void Ai_answer_payloads_carry_the_demo_label_and_the_demonstration_notice_by_default()
    {
        var range = new DateRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        var period = PeriodDto.Create(PeriodFilters.BuildPeriodInfo(range));

        var answer = new AiChatResponse(
            DemoData: true,
            Answer: "پاسخ",
            Period: period,
            Filters: new FiltersDto(null),
            View: "sales_overview",
            Facts: Array.Empty<string>(),
            Interpretation: string.Empty,
            Unavailable: false);

        Assert.Equal("demo", answer.DataLabel);
        Assert.Equal(AiDataLabel.Notice, answer.DemoNotice);
        Assert.Contains("دمو", answer.DemoNotice);

        var unavailable = new AiChatUnavailableResponse(
            DemoData: true,
            Unavailable: true,
            Answer: "پاسخی ارائه نشد",
            Message: "سرویس هوش مصنوعی پیکربندی نشده است.",
            Period: period,
            Filters: new FiltersDto(null),
            Facts: Array.Empty<string>(),
            Interpretation: string.Empty,
            View: null);

        Assert.Equal("demo", unavailable.DataLabel);
        Assert.Equal(AiDataLabel.Notice, unavailable.DemoNotice);
    }

    // ---- Attribute placement (T069): every Demo 1 and AI endpoint, not the purchasing ones ----

    private static bool HasDemoOnly(MemberInfo member) =>
        member.GetCustomAttributes<DemoOnlyAttribute>(inherit: true).Any();

    [Theory]
    [InlineData(typeof(SalesController))]
    [InlineData(typeof(InventoryController))]
    [InlineData(typeof(ReceivablesController))]
    [InlineData(typeof(MetaController))]
    [InlineData(typeof(AiDemoController))]
    public void Demo_and_ai_controllers_carry_demo_only(Type controller)
    {
        Assert.True(HasDemoOnly(controller), $"{controller.Name} must carry [DemoOnly].");
    }

    [Theory]
    [InlineData(typeof(OverviewController))]
    [InlineData(typeof(SyncAdminController))]
    [InlineData(typeof(SystemStatusController))]
    [InlineData(typeof(AuthController))]
    [InlineData(typeof(AiController))]
    [InlineData(typeof(AiAdminController))]
    public void Purchasing_and_platform_controllers_do_not_carry_demo_only(Type controller)
    {
        Assert.False(HasDemoOnly(controller), $"{controller.Name} must stay available in production.");
    }

    [Fact]
    public void Admin_demo_routes_are_marked_but_not_the_sync_routes()
    {
        Assert.False(HasDemoOnly(typeof(AdminController)), "AdminController is marked per action, not as a whole.");

        Assert.True(HasDemoOnly(typeof(AdminController).GetMethod(nameof(AdminController.GetProviderStatus))!));
        Assert.True(HasDemoOnly(typeof(AdminController).GetMethod(nameof(AdminController.GetReconciliation))!));

        foreach (var method in typeof(SyncAdminController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            Assert.False(HasDemoOnly(method), $"SyncAdminController.{method.Name} must not be demo-only.");
        }
    }
}
