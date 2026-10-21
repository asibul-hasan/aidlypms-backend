using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace AidlyPms.Api.Tests;

public static class TestControllerFactory
{
    public static TController Create<TController>(
        Func<IServiceProvider, TController> factory,
        IDbConnectionFactory db,
        TestTenantContext tenant) where TController : BaseController
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton(db);
        var sp = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = sp
        };

        var controller = factory(sp);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }
}
