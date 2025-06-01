//-------------------------------------------------------------------------------
// <copyright file="OperationType.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    /// <summary>
    /// Types of repository operations
    /// </summary>
    internal enum OperationType
    {
        Read,
        Insert,
        Update,
        Delete
    }
}