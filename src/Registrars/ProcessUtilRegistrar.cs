﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Soenneker.Utils.Process.Abstract;

namespace Soenneker.Utils.Process.Registrars;

/// <summary>
/// A utility library implementing useful process operations
/// </summary>
public static class ProcessUtilRegistrar
{
    /// <summary>
    /// Adds <see cref="IProcessUtil"/> as a scoped service. (Recommended) <para/>
    /// </summary>
    public static IServiceCollection AddProcessUtilAsScoped(this IServiceCollection services)
    {
        services.TryAddScoped<IProcessUtil, ProcessUtil>();

        return services;
    }

    /// <summary>
    /// Adds <see cref="IProcessUtil"/> as a singleton service. <para/>
    /// (Use <see cref="AddProcessUtilAsScoped"/> unless this is being consumed by a Singleton)
    /// </summary>
    public static IServiceCollection AddProcessUtilAsSingleton(this IServiceCollection services)
    {
        services.TryAddSingleton<IProcessUtil, ProcessUtil>();

        return services;
    }
}