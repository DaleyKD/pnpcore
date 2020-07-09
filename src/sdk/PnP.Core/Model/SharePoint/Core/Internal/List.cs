﻿using Microsoft.Extensions.Logging;
using PnP.Core.Services;
using PnP.Core.Utilities;
using System;
using System.Dynamic;
using System.Linq.Expressions;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace PnP.Core.Model.SharePoint
{
    /// <summary>
    /// List class, write your custom code here
    /// </summary>
    [SharePointType("SP.List", Uri = "_api/Web/Lists(guid'{Id}')", Update = "_api/web/lists/getbyid(guid'{Id}')", LinqGet = "_api/web/lists")]
    [GraphType(Get = "sites/{Parent.GraphId}/lists/{GraphId}", LinqGet = "sites/{Parent.GraphId}/lists")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2243:Attribute string literals should parse correctly", Justification = "<Pending>")]
    internal partial class List
    {
        public List()
        {
            MappingHandler = (FromJson input) =>
            {
                // Handle the mapping from json to the domain model for the cases which are not generically handled
                switch (input.TargetType.Name)
                {
                    case "ListExperience": return JsonMappingHelper.ToEnum<ListExperience>(input.JsonElement);
                    case "ListReadingDirection": return JsonMappingHelper.ToEnum<ListReadingDirection>(input.JsonElement);
                    case "ListTemplateType": return JsonMappingHelper.ToEnum<ListTemplateType>(input.JsonElement);
                }

                input.Log.LogDebug($"Field {input.FieldName} could not be mapped when converting from JSON");

                return null;
            };

            // Handler to construct the Add request for this list
            AddApiCallHandler = (additionalInformation) =>
            {
                var entity = EntityManager.Instance.GetClassInfo(GetType(), this);

                var addParameters = new
                {
                    __metadata = new { type = entity.SharePointType },
                    BaseTemplate = TemplateType,
                    Title
                }.AsExpando();
                string body = JsonSerializer.Serialize(addParameters, typeof(ExpandoObject));
                return new ApiCall($"_api/web/lists", ApiType.SPORest, body);
            };

            /** 
            // Sample handler that shows how to override the API call used for the delete of this entity
            DeleteApiCallOverrideHandler = (ApiCall apiCall) =>
            {
                return apiCall;
            };
            */

            /**
            // Sample update validation handler, can be used to prevent updates to a field (e.g. field validation, make readonly field, ...)
            ValidateUpdateHandler = (ref FieldUpdateRequest fieldUpdateRequest) => 
            {
                if (fieldUpdateRequest.FieldName == "Description")
                {
                    // Cancel update
                    //fieldUpdateRequest.CancelUpdate();

                    // Set other value to the field
                    //fieldUpdateRequest.Value = "bla";
                }
            };
            */
        }

        #region Extension methods

        #region BatchGetByTitle
        private static ApiCall GetByTitleApiCall(string title)
        {
            return new ApiCall($"_api/web/lists/getbytitle('{title}')", ApiType.SPORest);
        }

        internal IList BatchGetByTitle(Batch batch, string title, params Expression<Func<IList, object>>[] expressions)
        {
            BaseBatchGet(batch, apiOverride: GetByTitleApiCall(title), fromJsonCasting: MappingHandler, postMappingJson: PostMappingHandler, expressions: expressions);
            return this;
        }
        #endregion

        #region RecycleAsync
        public async Task<Guid> RecycleAsync()
        {
            var apiCall = new ApiCall($"_api/Web/Lists(guid'{Id}')/recycle", ApiType.SPORest);

            var response = await RawRequestAsync(apiCall, HttpMethod.Post).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(response.Json))
            {
                var document = JsonSerializer.Deserialize<JsonElement>(response.Json);
                if (document.TryGetProperty("d", out JsonElement root))
                {
                    if (root.TryGetProperty("Recycle", out JsonElement recycleBinItemId))
                    {
                        // Remove this item from the lists collection
                        RemoveFromParentCollection();

                        // return the recyclebin item id
                        return recycleBinItemId.GetGuid();
                    }
                }
            }

            return Guid.Empty;
        }
        #endregion

        #region GetItemsByCamlQuery
        public async Task<IListItemCollection> GetItemsByCamlQueryAsync(string query)
        {
            return await GetItemsByCamlQueryAsync(new CamlQueryOptions() { ViewXml = query }).ConfigureAwait(false);
        }

        public async Task<IListItemCollection> GetItemsByCamlQueryAsync(CamlQueryOptions queryOptions)
        {
            ApiCall apiCall = BuildGetItemsByCamlQueryApiCall(queryOptions);

            await RequestAsync(apiCall, HttpMethod.Post).ConfigureAwait(false);

            return Items;
        }

        public IListItemCollection GetItemsByCamlQuery(string query)
        {
            return GetItemsByCamlQuery(new CamlQueryOptions() { ViewXml = query });
        }

        public IListItemCollection GetItemsByCamlQuery(CamlQueryOptions queryOptions)
        {
            ApiCall apiCall = BuildGetItemsByCamlQueryApiCall(queryOptions);

            Request(apiCall, HttpMethod.Post);

            return Items;
        }

        public IListItemCollection GetItemsByCamlQuery(Batch batch, string query)
        {
            return GetItemsByCamlQuery(batch, new CamlQueryOptions() { ViewXml = query });
        }

        public IListItemCollection GetItemsByCamlQuery(Batch batch, CamlQueryOptions queryOptions)
        {
            ApiCall apiCall = BuildGetItemsByCamlQueryApiCall(queryOptions);

            Request(batch, apiCall, HttpMethod.Post);

            return Items;
        }

        private ApiCall BuildGetItemsByCamlQueryApiCall(CamlQueryOptions queryOptions)
        {
            // Build body
            var camlQuery = new
            {
                query = new
                {
                    __metadata = new { type = "SP.CamlQuery" },
                    queryOptions.ViewXml,
                    queryOptions.AllowIncrementalResults,
                    queryOptions.DatesInUtc,
                    queryOptions.FolderServerRelativeUrl,
                    queryOptions.ListItemCollectionPosition
                }
            }.AsExpando();
            string body = JsonSerializer.Serialize(camlQuery, typeof(ExpandoObject), new JsonSerializerOptions() { IgnoreNullValues = true });

            var apiCall = new ApiCall($"_api/Web/Lists(guid'{Id}')/GetItems", ApiType.SPORest, body, "Items");
            return apiCall;
        }
        #endregion

        #endregion
    }
}