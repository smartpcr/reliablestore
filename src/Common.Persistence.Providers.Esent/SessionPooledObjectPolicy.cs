//-------------------------------------------------------------------------------
// <copyright file="SessionPooledObjectPolicy.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.Esent
{
    using Microsoft.Extensions.ObjectPool;
    using Microsoft.Isam.Esent.Interop;

    internal class SessionPooledObjectPolicy : IPooledObjectPolicy<Session>
    {
        private readonly Instance instance;

        public SessionPooledObjectPolicy(Instance instance)
        {
            this.instance = instance;
        }

        public Session Create()
        {
            return new Session(this.instance);
        }

        public bool Return(Session obj)
        {
            if (obj == null)
            {
                return false;
            }

            try
            {
                // Reset session state if needed
                // Sessions are lightweight and don't need special cleanup
                return true;
            }
            catch
            {
                // If there's any issue, don't return to pool
                return false;
            }
        }
    }
}