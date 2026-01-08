using System.Collections.Generic;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnityTests.Editor.Helpers
{
    /// <summary>
    /// Tests for the standard Pagination classes.
    /// </summary>
    public class PaginationTests
    {
        #region PaginationRequest Tests

        [Test]
        public void PaginationRequest_FromParams_ParsesPageSizeSnakeCase()
        {
            var p = new JObject { ["page_size"] = 25 };
            var req = PaginationRequest.FromParams(p);
            Assert.AreEqual(25, req.PageSize);
        }

        [Test]
        public void PaginationRequest_FromParams_ParsesPageSizeCamelCase()
        {
            var p = new JObject { ["pageSize"] = 30 };
            var req = PaginationRequest.FromParams(p);
            Assert.AreEqual(30, req.PageSize);
        }

        [Test]
        public void PaginationRequest_FromParams_ParsesCursor()
        {
            var p = new JObject { ["cursor"] = 50 };
            var req = PaginationRequest.FromParams(p);
            Assert.AreEqual(50, req.Cursor);
        }

        [Test]
        public void PaginationRequest_FromParams_ConvertsPageNumberToCursor()
        {
            // page_number is 1-based, should convert to 0-based cursor
            var p = new JObject { ["page_number"] = 3, ["page_size"] = 10 };
            var req = PaginationRequest.FromParams(p);
            // Page 3 with page size 10 means items 20-29, so cursor should be 20
            Assert.AreEqual(20, req.Cursor);
        }

        [Test]
        public void PaginationRequest_FromParams_CursorTakesPrecedenceOverPageNumber()
        {
            // If both cursor and page_number are specified, cursor should win
            var p = new JObject { ["cursor"] = 100, ["page_number"] = 1 };
            var req = PaginationRequest.FromParams(p);
            Assert.AreEqual(100, req.Cursor);
        }

        [Test]
        public void PaginationRequest_FromParams_UsesDefaultsForNullParams()
        {
            var req = PaginationRequest.FromParams(null);
            Assert.AreEqual(50, req.PageSize);
            Assert.AreEqual(0, req.Cursor);
        }

        [Test]
        public void PaginationRequest_FromParams_UsesDefaultsForEmptyParams()
        {
            var req = PaginationRequest.FromParams(new JObject());
            Assert.AreEqual(50, req.PageSize);
            Assert.AreEqual(0, req.Cursor);
        }

        [Test]
        public void PaginationRequest_FromParams_AcceptsCustomDefaultPageSize()
        {
            var req = PaginationRequest.FromParams(new JObject(), defaultPageSize: 100);
            Assert.AreEqual(100, req.PageSize);
        }

        [Test]
        public void PaginationRequest_FromParams_HandleStringValues()
        {
            // Some clients might send string values
            var p = new JObject { ["page_size"] = "15", ["cursor"] = "5" };
            var req = PaginationRequest.FromParams(p);
            Assert.AreEqual(15, req.PageSize);
            Assert.AreEqual(5, req.Cursor);
        }

        #endregion

        #region PaginationResponse Tests

        [Test]
        public void PaginationResponse_Create_ReturnsCorrectPageOfItems()
        {
            var allItems = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            var request = new PaginationRequest { PageSize = 3, Cursor = 0 };

            var response = PaginationResponse<int>.Create(allItems, request);

            Assert.AreEqual(3, response.Items.Count);
            Assert.AreEqual(new List<int> { 1, 2, 3 }, response.Items);
        }

        [Test]
        public void PaginationResponse_Create_ReturnsCorrectMiddlePage()
        {
            var allItems = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            var request = new PaginationRequest { PageSize = 3, Cursor = 3 };

            var response = PaginationResponse<int>.Create(allItems, request);

            Assert.AreEqual(3, response.Items.Count);
            Assert.AreEqual(new List<int> { 4, 5, 6 }, response.Items);
        }

        [Test]
        public void PaginationResponse_Create_HandlesLastPage()
        {
            var allItems = new List<int> { 1, 2, 3, 4, 5 };
            var request = new PaginationRequest { PageSize = 3, Cursor = 3 };

            var response = PaginationResponse<int>.Create(allItems, request);

            Assert.AreEqual(2, response.Items.Count);
            Assert.AreEqual(new List<int> { 4, 5 }, response.Items);
            Assert.IsNull(response.NextCursor);
            Assert.IsFalse(response.HasMore);
        }

        [Test]
        public void PaginationResponse_HasMore_TrueWhenNextCursorSet()
        {
            var allItems = new List<int> { 1, 2, 3, 4, 5, 6 };
            var request = new PaginationRequest { PageSize = 3, Cursor = 0 };

            var response = PaginationResponse<int>.Create(allItems, request);

            Assert.IsTrue(response.HasMore);
            Assert.AreEqual(3, response.NextCursor);
        }

        [Test]
        public void PaginationResponse_HasMore_FalseWhenNoMoreItems()
        {
            var allItems = new List<int> { 1, 2, 3 };
            var request = new PaginationRequest { PageSize = 10, Cursor = 0 };

            var response = PaginationResponse<int>.Create(allItems, request);

            Assert.IsFalse(response.HasMore);
            Assert.IsNull(response.NextCursor);
        }

        [Test]
        public void PaginationResponse_Create_SetsCorrectTotalCount()
        {
            var allItems = new List<string> { "a", "b", "c", "d", "e" };
            var request = new PaginationRequest { PageSize = 2, Cursor = 0 };

            var response = PaginationResponse<string>.Create(allItems, request);

            Assert.AreEqual(5, response.TotalCount);
        }

        [Test]
        public void PaginationResponse_Create_HandlesEmptyList()
        {
            var allItems = new List<int>();
            var request = new PaginationRequest { PageSize = 10, Cursor = 0 };

            var response = PaginationResponse<int>.Create(allItems, request);

            Assert.AreEqual(0, response.Items.Count);
            Assert.AreEqual(0, response.TotalCount);
            Assert.IsNull(response.NextCursor);
            Assert.IsFalse(response.HasMore);
        }

        [Test]
        public void PaginationResponse_Create_ClampsCursorToValidRange()
        {
            var allItems = new List<int> { 1, 2, 3 };
            var request = new PaginationRequest { PageSize = 10, Cursor = 100 };

            var response = PaginationResponse<int>.Create(allItems, request);

            Assert.AreEqual(0, response.Items.Count);
            Assert.AreEqual(3, response.Cursor); // Clamped to totalCount
        }

        [Test]
        public void PaginationResponse_Create_HandlesNegativeCursor()
        {
            var allItems = new List<int> { 1, 2, 3 };
            var request = new PaginationRequest { PageSize = 10, Cursor = -5 };

            var response = PaginationResponse<int>.Create(allItems, request);

            Assert.AreEqual(0, response.Cursor); // Clamped to 0
            Assert.AreEqual(3, response.Items.Count);
        }

        #endregion
    }
}

