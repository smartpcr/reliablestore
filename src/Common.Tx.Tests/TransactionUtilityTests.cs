// ------------------------------------------------------------------------------
// <copyright file="TransactionUtilityTests.cs" company="Your Company">
//     Copyright (c) Your Company. All rights reserved.
// ------------------------------------------------------------------------------

namespace Common.Tx.Tests
{
    using System;
    using System.Collections.Generic;
    using Common.Tx;
    using Microsoft.Extensions.Logging;
    using Moq;
    using Unity;
    using Xunit;

    public sealed class TransactionUtilityTests
    {
        [Fact]
        public void TransactionOptions_Defaults_AreCorrect()
        {
            var options = new TransactionOptions();
            Assert.Equal(IsolationLevel.ReadCommitted, options.IsolationLevel);
            Assert.Equal(TimeSpan.FromMinutes(5), options.Timeout);
            Assert.Null(options.ResourceOperationTimeout);
            Assert.True(options.EnableSavepoints);
            Assert.True(options.AutoRollbackOnDispose);
            Assert.NotNull(options.Properties);
        }

        [Fact]
        public void TransactionException_Constructors_SetProperties()
        {
            var ex1 = new TransactionException("msg");
            Assert.Equal("msg", ex1.Message);
            Assert.Null(ex1.TransactionState);

            var inner = new Exception("inner");
            var ex2 = new TransactionException("msg2", inner);
            Assert.Equal("msg2", ex2.Message);
            Assert.Equal(inner, ex2.InnerException);

            var ex3 = new TransactionException("msg3", TransactionState.Failed);
            Assert.Equal(TransactionState.Failed, ex3.TransactionState);

            var ex4 = new TransactionException("msg4", TransactionState.Timeout, inner);
            Assert.Equal(TransactionState.Timeout, ex4.TransactionState);
            Assert.Equal(inner, ex4.InnerException);
        }

        [Fact]
        public void TransactionTimeoutException_SetsTimeoutState()
        {
            var ex = new TransactionTimeoutException("timeout");
            Assert.Equal(TransactionState.Timeout, ex.TransactionState);
            Assert.Equal("timeout", ex.Message);
        }
    }
}

