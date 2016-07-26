﻿// -----------------------------------------------------------------------
//  <copyright file="DocumentHandler.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Primitives;
using Raven.Abstractions.Data;
using Raven.Server.Documents.Includes;
using Raven.Server.Documents.Patch;
using Raven.Server.Documents.Transformers;
using Raven.Server.Json;
using Raven.Server.Routing;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Sparrow;
using Sparrow.Json;
using Voron.Exceptions;

namespace Raven.Server.Documents.Handlers
{
    public class DocumentHandler : DatabaseRequestHandler
    {
        [RavenAction("/databases/*/docs", "HEAD", "/databases/{databaseName:string}/docs?id={documentId:string}")]
        public Task Head()
        {
            var id = GetQueryStringValueAndAssertIfSingleAndNotEmpty("id");

            DocumentsOperationContext context;
            using (ContextPool.AllocateOperationContext(out context))
            using (context.OpenReadTransaction())
            {
                var document = Database.DocumentsStorage.Get(context, id);
                if (document == null)
                    HttpContext.Response.StatusCode = 404;
                else
                    HttpContext.Response.Headers[Constants.MetadataEtagField] = document.Etag.ToString();
                return Task.CompletedTask;
            }
        }

        [RavenAction("/databases/*/docs", "GET", "/databases/{databaseName:string}/docs?id={documentId:string|multiple}&include={fieldName:string|optional|multiple}&transformer={transformerName:string|optional}")]
        public Task Get()
        {
            var ids = HttpContext.Request.Query["id"];
            var metadataOnly = GetBoolValueQueryString("metadata-only", required: false) ?? false;
            var transformerName = GetStringQueryString("transformer", required: false);

            Transformer transformer = null;
            if (string.IsNullOrEmpty(transformerName) == false)
            {
                transformer = Database.TransformerStore.GetTransformer(transformerName);
                if (transformer == null)
                    throw new InvalidOperationException("No transformer with the name: " + transformerName);
            }

            DocumentsOperationContext context;
            using (ContextPool.AllocateOperationContext(out context))
            using (context.OpenReadTransaction())
            {
                if (ids.Count > 0)
                    GetDocumentsById(context, ids, transformer, metadataOnly);
                else
                    GetDocuments(context, transformer, metadataOnly);

                return Task.CompletedTask;
            }
        }

        [RavenAction("/databases/*/docs", "POST", "/databases/{databaseName:string}/docs body{documentsIds:string[]}")]
        public async Task PostGet()
        {
            var metadataOnly = GetBoolValueQueryString("metadata-only", required: false) ?? false;

            DocumentsOperationContext context;
            using (ContextPool.AllocateOperationContext(out context))
            {
                var array = await context.ParseArrayToMemoryAsync(RequestBodyStream(), "docs", BlittableJsonDocumentBuilder.UsageMode.None);

                var ids = new string[array.Length];
                for (int i = 0; i < array.Length; i++)
                {
                    ids[i] = array.GetStringByIndex(i);
                }

                context.OpenReadTransaction();
                GetDocumentsById(context, new StringValues(ids), null, metadataOnly);
            }
        }

        private void GetDocuments(DocumentsOperationContext context, Transformer transformer, bool metadataOnly)
        {
            // everything here operates on all docs
            var actualEtag = ComputeAllDocumentsEtag(context);

            if (GetLongFromHeaders("If-None-Match") == actualEtag)
            {
                HttpContext.Response.StatusCode = 304;
                return;
            }
            HttpContext.Response.Headers["ETag"] = actualEtag.ToString();

            var etag = GetLongQueryString("etag", false);
            IEnumerable<Document> documents;
            if (etag != null)
            {
                documents = Database.DocumentsStorage.GetDocumentsAfter(context, etag.Value, GetStart(), GetPageSize());
            }
            else if (HttpContext.Request.Query.ContainsKey("startsWith"))
            {
                documents = Database.DocumentsStorage.GetDocumentsStartingWith(context,
                    HttpContext.Request.Query["startsWith"],
                    HttpContext.Request.Query["matches"],
                    HttpContext.Request.Query["excludes"],
                    GetStart(),
                    GetPageSize()
                    );
            }
            else // recent docs
            {
                documents = Database.DocumentsStorage.GetDocumentsInReverseEtagOrder(context, GetStart(), GetPageSize());
            }

            using (var writer = new BlittableJsonTextWriter(context, ResponseBodyStream()))
            {
                if (transformer != null)
                {
                    using (var scope = transformer.OpenTransformationScope(null, null, Database.DocumentsStorage, context))
                    {
                        writer.WriteDocuments(context, scope.Transform(documents), metadataOnly);
                        return;
                    }
                }

                writer.WriteDocuments(context, documents, metadataOnly);
            }
        }

        private void GetDocumentsById(DocumentsOperationContext context, StringValues ids, Transformer transformer, bool metadataOnly)
        {
            /* TODO: Call AddRequestTraceInfo
            AddRequestTraceInfo(sb =>
            {
                foreach (var id in ids)
                {
                    sb.Append("\t").Append(id).AppendLine();
                }
            });*/
            var includePaths = HttpContext.Request.Query["include"];
            var documents = new List<Document>(ids.Count);
            var includes = new List<Document>(includePaths.Count * ids.Count);
            var includeDocs = new IncludeDocumentsCommand(Database.DocumentsStorage, context, includePaths);
            foreach (var id in ids)
            {
                var document = Database.DocumentsStorage.Get(context, id);
                documents.Add(document);
                includeDocs.Gather(document);
            }

            long actualEtag = ComputeEtagsFor(documents);
            if (GetLongFromHeaders("If-None-Match") == actualEtag)
            {
                HttpContext.Response.StatusCode = 304;
                return;
            }

            HttpContext.Response.Headers["Content-Type"] = "application/json; charset=utf-8";
            HttpContext.Response.Headers["ETag"] = actualEtag.ToString();
            using (var writer = new BlittableJsonTextWriter(context, ResponseBodyStream()))
            {
                writer.WriteStartObject();
                writer.WritePropertyName(context.GetLazyStringForFieldWithCaching("Results"));

                if (transformer != null)
                {
                    using (var scope = transformer.OpenTransformationScope(null, includeDocs, Database.DocumentsStorage, context))
                    {
                        writer.WriteDocuments(context, scope.Transform(documents), metadataOnly);
                    }
                }
                else
                {
                    writer.WriteDocuments(context, documents, metadataOnly);
                }

                includeDocs.Fill(includes);

                writer.WriteComma();
                writer.WritePropertyName(context.GetLazyStringForFieldWithCaching("Includes"));

                if (includePaths.Count > 0)
                {
                    writer.WriteDocuments(context, includes, metadataOnly);
                }
                else
                {
                    writer.WriteStartArray();
                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }
        }

        private unsafe long ComputeEtagsFor(List<Document> documents)
        {
            // This method is efficient because we aren't materializing any values
            // except the etag, which we need
            if (documents.Count == 1)
            {
                return documents[0]?.Etag ?? -1;
            }
            // we do this in a loop to avoid either large long array allocation on the heap
            // or busting the stack if we used stackalloc long[ids.Count]
            var ctx = Hashing.Streamed.XXHash64.BeginProcess();
            long* buffer = stackalloc long[4];//32 bytes
            Memory.Set((byte*)buffer, 0, sizeof(long) * 4);// not sure is stackalloc force init
            for (int i = 0; i < documents.Count; i += 4)
            {
                for (int j = 0; j < 4; j++)
                {
                    if (i + j >= documents.Count)
                        break;
                    var document = documents[i + j];
                    buffer[j] = document?.Etag ?? -1;
                }
                // we don't care if we didn't get to the end and have values from previous iteration
                // it will still be consistent, and that is what we care here.
                ctx = Hashing.Streamed.XXHash64.Process(ctx, (byte*)buffer, sizeof(long) * 4);
            }
            return (long)Hashing.Streamed.XXHash64.EndProcess(ctx);
        }

        private unsafe long ComputeAllDocumentsEtag(DocumentsOperationContext context)
        {
            var buffer = stackalloc long[2];

            buffer[0] = DocumentsStorage.ReadLastEtag(context.Transaction.InnerTransaction);
            buffer[1] = Database.DocumentsStorage.GetNumberOfDocuments(context);

            return (long)Hashing.XXHash64.Calculate((byte*)buffer, sizeof(long) * 2);
        }

        private class MergedDeleteCommand : TransactionOperationsMerger.MergedTransactionCommand
        {
            public string Key;
            public long? ExepctedEtag;
            public DocumentDatabase Database;
            public ExceptionDispatchInfo ExceptionDispatchInfo;

            public override void Execute(DocumentsOperationContext context, RavenTransaction tx)
            {
                try
                {
                    Database.DocumentsStorage.Delete(context, Key, ExepctedEtag);
                }
                catch (ConcurrencyException e)
                {
                    ExceptionDispatchInfo = ExceptionDispatchInfo.Capture(e);
                }
            }
        }

        [RavenAction("/databases/*/docs", "DELETE", "/databases/{databaseName:string}/docs?id={documentId:string}")]
        public async Task Delete()
        {
            DocumentsOperationContext context;
            using (ContextPool.AllocateOperationContext(out context))
            {
                var id = GetQueryStringValueAndAssertIfSingleAndNotEmpty("id");

                var etag = GetLongFromHeaders("If-Match");

                var cmd = new MergedDeleteCommand
                {
                    Key = id,
                    Database = Database,
                    ExepctedEtag = etag
                };

                await Database.TxMerger.Enqueue(cmd);

                cmd.ExceptionDispatchInfo?.Throw();

                HttpContext.Response.StatusCode = 204; // NoContent
            }
        }

        private class MergedPutCommand : TransactionOperationsMerger.MergedTransactionCommand
        {
            public string Key;
            public long? ExepctedEtag;
            public BlittableJsonReaderObject Document;
            public DocumentDatabase Database;
            public ExceptionDispatchInfo ExceptionDispatchInfo;
            public PutResult PutResult;

            public override void Execute(DocumentsOperationContext context, RavenTransaction tx)
            {
                try
                {
                    PutResult = Database.DocumentsStorage.Put(context, Key, ExepctedEtag, Document);
                }
                catch (ConcurrencyException e)
                {
                    ExceptionDispatchInfo = ExceptionDispatchInfo.Capture(e);
                }
            }
        }

        [RavenAction("/databases/*/docs", "PUT", "/databases/{databaseName:string}/docs?id={documentId:string}")]
        public async Task Put()
        {
            DocumentsOperationContext context;
            using (ContextPool.AllocateOperationContext(out context))
            {
                var id = GetQueryStringValueAndAssertIfSingleAndNotEmpty("id");

                var doc = await context.ReadForMemoryAsync(RequestBodyStream(), id);

                var etag = GetLongFromHeaders("If-Match");

                var cmd = new MergedPutCommand
                {
                    Database = Database,
                    ExepctedEtag = etag,
                    Key = id,
                    Document = doc
                };

                await Database.TxMerger.Enqueue(cmd);

                cmd.ExceptionDispatchInfo?.Throw();

                HttpContext.Response.StatusCode = 201;

                using (var writer = new BlittableJsonTextWriter(context, ResponseBodyStream()))
                {
                    writer.WriteStartObject();

                    writer.WritePropertyName(context.GetLazyString("Key"));
                    writer.WriteString(context.GetLazyString(cmd.PutResult.Key));
                    writer.WriteComma();

                    writer.WritePropertyName(context.GetLazyString("Etag"));
                    writer.WriteInteger(cmd.PutResult.ETag ?? -1);

                    writer.WriteEndObject();
                }
            }
        }

        [RavenAction("/databases/*/docs", "PATCH", "/databases/{databaseName:string}/docs?id={documentId:string}&test={isTestOnly:bool|optional(false)} body{ Patch:PatchRequest, PatchIfMissing:PatchRequest }")]
        public Task Patch()
        {
            var id = GetQueryStringValueAndAssertIfSingleAndNotEmpty("ids");

            var etag = GetLongFromHeaders("If-Match");
            var isTestOnly = GetBoolValueQueryString("test", required: false) ?? false;

            DocumentsOperationContext context;
            using (ContextPool.AllocateOperationContext(out context))
            {
                var request = context.Read(RequestBodyStream(), "ScriptedPatchRequest");

                BlittableJsonReaderObject patchCmd, patchIsMissingCmd;
                if (request.TryGet("Patch", out patchCmd) == false)
                    throw new ArgumentException("The 'Patch' field in the body request is mandatory");
                var patch = PatchRequest.Parse(patchCmd);

                PatchRequest patchIfMissing = null;
                if (request.TryGet("PatchIfMissing", out patchIsMissingCmd))
                {
                    patchIfMissing = PatchRequest.Parse(patchCmd);
                }

                // TODO: In order to properly move this to the transaction merger, we need
                // TODO: move a lot of the costs (such as script parsing) out, so we create
                // TODO: an object that we'll apply, otherwise we'll slow down a lot the transactions
                // TODO: just by doing the javascript parsing and preparing the engine

                PatchResultData patchResult;
                using (context.OpenWriteTransaction())
                {
                    patchResult = Database.Patch.Apply(context, id, etag, patch, patchIfMissing, isTestOnly);
                    context.Transaction.Commit();
                }

                Debug.Assert(patchResult.PatchResult == PatchResult.Patched == isTestOnly == false);

                using (var writer = new BlittableJsonTextWriter(context, ResponseBodyStream()))
                {
                    writer.WriteStartObject();

                    writer.WritePropertyName(context.GetLazyString("Patched"));
                    writer.WriteBool(isTestOnly == false);
                    writer.WriteComma();

                    writer.WritePropertyName(context.GetLazyString("Debug"));
                    writer.WriteObject(patchResult.ModifiedDocument);

                    if (isTestOnly)
                    {
                        writer.WriteComma();
                        writer.WritePropertyName(context.GetLazyString("Document"));
                        writer.WriteObject(patchResult.OriginalDocument);
                    }

                    writer.WriteEndObject();
                }
            }

            return Task.CompletedTask;
        }
    }
}