﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unisave.Database;
using Unisave.Exceptions;

namespace Unisave.Database
{
    /// <summary>
    /// This class is placed at the framework endpoint to notify the developer
    /// whenever they try to access the database locally
    /// </summary>
    public class FakeDatabase : IDatabase
    {
        public static void NotifyDeveloper()
        {
            throw new UnisaveException(
                "You cannot query, load or save entities from client-side code. " +
                "This can only be done from inside facets.\n" +
                "You can however return entities from facets, so you can work with them on the client side."
            );
        }

        /// <inheritdoc/>
        public void SaveEntity(RawEntity entity)
        {
            NotifyDeveloper();
        }

        /// <inheritdoc/>
        public RawEntity LoadEntity(string id)
        {
            NotifyDeveloper();
            return null;
        }

        /// <inheritdoc/>
        public bool DeleteEntity(string id)
        {
            NotifyDeveloper();
            return false;
        }

        /// <inheritdoc/>
        public IEnumerable<RawEntity> QueryEntities(string entityType, EntityQuery query)
        {
            NotifyDeveloper();
            yield break;
        }
    }
}