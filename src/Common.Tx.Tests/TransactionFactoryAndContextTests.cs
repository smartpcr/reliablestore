// ------------------------------------------------------------------------------
// <copyright file="TransactionFactoryAndContextTests.cs" company="Your Company">
//     Copyright (c) Your Company. All rights reserved.
// </copyright>
// ------------------------------------------------------------------------------

namespace Common.Tx.Tests
{
    using System;
    using Common.Tx;
    using Microsoft.Extensions.Logging;
    using Moq;
    using Xunit;

    /// <summary>
    /// Contains unit tests for TransactionFactory and TransactionContext.
    /// </summary>
    public sealed class TransactionFactoryAndContextTests
    {
        [Fact]
        public void TransactionFactory_CreatesTransaction_WithLogger()
        {
            var loggerFactory = new Mock<ILoggerFactory>();
            var logger = new Mock<ILogger>();
            loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(logger.Object);
            var factory = new TransactionFactory(loggerFactory.Object);

            var transaction = factory.CreateTransaction();
            Assert.NotNull(transaction);
            Assert.IsType<TransactionCoordinator>(transaction);
        }

        [Fact]
        public void TransactionFactory_Current_ReturnsNull_WhenNoAmbientTransaction()
        {
            var loggerFactory = new Mock<ILoggerFactory>();
            var factory = new TransactionFactory(loggerFactory.Object);
            Assert.Null(factory.Current);
        }
    }
}

