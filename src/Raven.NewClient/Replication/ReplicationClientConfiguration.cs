// -----------------------------------------------------------------------
//  <copyright file="ReplicationClientConfiguration.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
namespace Raven.NewClient.Client.Replication
{
    public class ReplicationClientConfiguration
    {
        public FailoverBehavior? FailoverBehavior { get; set; }
    }
}
