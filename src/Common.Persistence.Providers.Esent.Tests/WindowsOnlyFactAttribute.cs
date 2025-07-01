//-------------------------------------------------------------------------------
// <copyright file="WindowsOnlyFactAttribute.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.Esent.Tests
{
    using System.Runtime.InteropServices;
    using Xunit;

    /// <summary>
    /// Custom Fact attribute that skips tests on non-Windows platforms.
    /// </summary>
    public class WindowsOnlyFactAttribute : FactAttribute
    {
        public WindowsOnlyFactAttribute()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                this.Skip = "ESENT is only available on Windows";
            }
        }
    }
}