// ------------------------------------------------------------------------------
// <copyright file="TransactionDiRegistrationTests.cs" company="Your Company">
//     Copyright (c) Your Company. All rights reserved.
// </copyright>
// ------------------------------------------------------------------------------

namespace Common.Tx.Tests
{
    using Common.Tx;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Moq;
    using Unity;
    using Xunit;

    public class TransactionDependencyInjectionTests
    {
        [Fact]
        public void UnityTransactionConfiguration_RegistersServices()
        {
            var container = new UnityContainer();
            container.RegisterInstance(typeof(ILoggerFactory), new Mock<ILoggerFactory>().Object);
            container.RegisterTransactionServices();
            var factory = container.Resolve<ITransactionFactory>();
            var repoFactory = container.Resolve<ITransactionalRepositoryFactory>();
            Assert.NotNull(factory);
            Assert.NotNull(repoFactory);
        }

        [Fact]
        public void TransactionServiceCollectionExtensions_RegistersServices()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory>(new Mock<ILoggerFactory>().Object);
            services.AddTransactionSupport();
            var provider = services.BuildServiceProvider();
            var factory = provider.GetService<ITransactionFactory>();
            var repoFactory = provider.GetService<ITransactionalRepositoryFactory>();
            Assert.NotNull(factory);
            Assert.NotNull(repoFactory);
        }
    }
}

