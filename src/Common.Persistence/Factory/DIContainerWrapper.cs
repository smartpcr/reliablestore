// -----------------------------------------------------------------------
// <copyright file="DIContainerWrapper.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Factory
{
    using System;
    using System.Reflection;
    using Common.Persistence.Configuration;
    using Common.Persistence.Contract;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Unity;

    public class DIContainerWrapper
    {
        public IServiceCollection? Services { get; }
        public IUnityContainer? UnityContainer { get; }

        public DIContainerWrapper(IServiceCollection services)
        {
            this.Services = services;
        }

        public DIContainerWrapper(IUnityContainer unityContainer)
        {
            this.UnityContainer = unityContainer;
        }
    }

    public static class DIContainerExtension
    {
        public static T TryRegisterAndGetRequired<T>(
            this DIContainerWrapper containerWrapper,
            string name,
            ConstructorInfo ctor) where T : class
        {
            if (containerWrapper.Services != null)
            {
                // Register a singleton named instance using reflection
                containerWrapper.Services.TryAddKeyedSingleton<T>(
                    name,
                    (sp, _) =>
                    {
                        return (T)ctor.Invoke(new object[] { sp, name });
                    });

                var serviceProvider = containerWrapper.Services.BuildServiceProvider();
                return serviceProvider.GetRequiredKeyedService<T>(name);
            }

            if (containerWrapper.UnityContainer != null)
            {
                containerWrapper.UnityContainer.RegisterInstance<T>(
                    name,
                    (T)ctor.Invoke(new object[] { containerWrapper.UnityContainer, name }));
                return containerWrapper.UnityContainer.Resolve<T>(name);
            }

            throw new InvalidOperationException($"Failed to find registration for provider '{name}'");
        }

        public static ConstructorInfo FindConstructor<T>(this BaseProviderSettings settings)
        {
            var assemblyName = settings.AssemblyName;
            var typeName = settings.TypeName;
            var assembly = AppDomain.CurrentDomain.Load(assemblyName);
            var genericType = assembly.GetType(typeName);
            if (genericType == null)
            {
                throw new InvalidOperationException($"Cannot find type '{typeName}' in assembly '{assemblyName}'");
            }
            var implType = genericType.MakeGenericType(typeof(T));
            var ctor = implType.GetConstructor(new[] { typeof(IServiceProvider), typeof(string) });
            if (ctor == null)
            {
                throw new InvalidOperationException($"No suitable constructor found for {implType}");
            }

            return ctor;
        }
    }
}