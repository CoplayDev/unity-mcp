using AssetStoreTools.Api.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace AssetStoreTools.Api.Responses
{
    internal class CategoryDataResponse : AssetStoreResponse
    {
        public List<Category> Categories { get; set; }

        public CategoryDataResponse() : base() { }
        public CategoryDataResponse(Exception e) : base(e) { }

        public CategoryDataResponse(string json)
        {
            try
            {
                var categoryArray = JsonConvert.DeserializeObject<JArray>(json);

                Categories = new List<Category>();
                var serializer = new JsonSerializer()
                {
                    ContractResolver = new Category.AssetStoreCategoryResolver()
                };

                foreach (var categoryData in categoryArray)
                {
                    var category = categoryData.ToObject<Category>(serializer);
                    Categories.Add(category);
                }

                Success = true;
            }
            catch (Exception e)
            {
                Success = false;
                Exception = e;
            }
        }
    }
}