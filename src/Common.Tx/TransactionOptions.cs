//-------------------------------------------------------------------------------
// <copyright file="TransactionOptions.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Transaction configuration options
    /// </summary>
    public class TransactionOptions
    {
        public IsolationLevel IsolationLevel { get; set; } = IsolationLevel.ReadCommitted;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
        public bool EnableSavepoints { get; set; } = true;
        public bool AutoRollbackOnDispose { get; set; } = true;
        public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
    }
}