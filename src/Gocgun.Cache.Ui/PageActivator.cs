using System.Reflection;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Gocgun.Cache.Ui;

internal sealed class PageActivator : IPageModelActivatorProvider
{
    public Func<PageContext, object> CreateActivator(CompiledPageActionDescriptor descriptor)
    {
        var modelType = descriptor.ModelTypeInfo?.AsType()
            ?? throw new InvalidOperationException("The page has no model.");
        var constructor = modelType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .OrderByDescending(candidate => candidate.GetParameters().Length)
            .First();
        var parameters = constructor.GetParameters();
        return context =>
        {
            var services = context.HttpContext.RequestServices;
            var args = new object[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                args[i] = services.GetRequiredService(parameters[i].ParameterType);
            }

            return constructor.Invoke(args);
        };
    }

    public Action<PageContext, object>? CreateReleaser(CompiledPageActionDescriptor descriptor)
    {
        return null;
    }
}
