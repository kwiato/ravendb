//-----------------------------------------------------------------------
// <copyright file="DeleteRemovedIndexes.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;

namespace Raven.Database.Plugins.Builtins
{
	public class DeleteRemovedIndexes : IStartupTask
	{
		#region IStartupTask Members

		public void Execute(DocumentDatabase database)
		{
			database.TransactionalStorage.BatchRead(actions =>
			{
				List<string> indexNames = actions.Indexing.GetIndexesStats().Select(x => x.Name).ToList();
				foreach (string indexName in indexNames)
				{
					if (database.IndexDefinitionStorage.Contains(indexName))
						continue;

					// index is not found on disk, better kill for good
					// Even though technically we are running into a situation that is considered to be corrupt data
					// we can safely recover from it by removing the other parts of the index.
					database.IndexStorage.DeleteIndex(indexName);
					actions.Indexing.DeleteIndex(indexName);
				}
			});
		}

		#endregion
	}
}
