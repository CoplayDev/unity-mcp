using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetStoreTools.Previews.UI.Elements
{
    internal class GridListElement : VisualElement
    {
        public int ElementWidth;
        public int ElementHeight;
        private int _visibilityHeadroom => ElementHeight;

        public IList ItemSource;
        public Func<VisualElement> MakeItem;
        public Action<VisualElement, int> BindItem;

        private ScrollView _scrollView;

        public GridListElement()
        {
            style.flexGrow = 1;

            Create();

            _scrollView.contentViewport.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            _scrollView.verticalScroller.valueChanged += OnVerticalScroll;
#if UNITY_2021_1_OR_NEWER
            _scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
#else
            _scrollView.showHorizontal = false;
#endif
        }

        private void Create()
        {
            _scrollView = new ScrollView();
            Add(_scrollView);
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            Redraw();
        }

        private void OnVerticalScroll(float value)
        {
            Redraw();
        }

        public void Redraw()
        {
            if (ElementWidth == 0
                || ElementHeight == 0
                || ItemSource == null
                || MakeItem == null
                || BindItem == null)
                return;

            _scrollView.Clear();

            var rowCapacity = Mathf.FloorToInt(_scrollView.contentContainer.worldBound.width / ElementWidth);
            if (rowCapacity == 0)
                rowCapacity = 1;

            var totalRequiredRows = ItemSource.Count / rowCapacity;
            if (ItemSource.Count % rowCapacity != 0)
                totalRequiredRows++;

            _scrollView.contentContainer.style.height = totalRequiredRows * ElementHeight;

            var visibleRows = new List<int>();
            for (int i = 0; i < totalRequiredRows; i++)
            {
                var visible = IsRowVisible(i);
                if (!visible)
                    continue;

                var rowElement = CreateRow(i);

                for (int j = 0; j < rowCapacity; j++)
                {
                    var elementIndex = i * rowCapacity + j;
                    if (elementIndex >= ItemSource.Count)
                    {
                        rowElement.Add(CreateFillerElement());
                        continue;
                    }

                    var element = MakeItem?.Invoke();
                    BindItem?.Invoke(element, elementIndex);

                    rowElement.Add(element);
                }

                _scrollView.Add(rowElement);
            }
        }

        private bool IsRowVisible(int rowIndex)
        {
            var contentStartY = _scrollView.contentContainer.worldBound.yMin;
            var visibleContentMinY = _scrollView.contentViewport.worldBound.yMin - _visibilityHeadroom;
            var visibleContentMaxY = _scrollView.contentViewport.worldBound.yMax + _visibilityHeadroom;
            if (_scrollView.contentViewport.worldBound.height == 0)
                visibleContentMaxY = this.worldBound.yMax;

            var rowMinY = (rowIndex * ElementHeight) + contentStartY;
            var rowMaxY = (rowIndex * ElementHeight) + ElementHeight + contentStartY;

            var fullyVisible = rowMinY >= visibleContentMinY && rowMaxY <= visibleContentMaxY;
            var partiallyAbove = rowMinY < visibleContentMinY && rowMaxY > visibleContentMinY;
            var partiallyBelow = rowMaxY > visibleContentMaxY && rowMinY < visibleContentMaxY;

            return fullyVisible || partiallyAbove || partiallyBelow;
        }

        private VisualElement CreateRow(int rowIndex)
        {
            var rowElement = new VisualElement() { name = $"Row {rowIndex}" };
            rowElement.style.flexDirection = FlexDirection.Row;
            rowElement.style.position = Position.Absolute;
            rowElement.style.top = ElementHeight * rowIndex;
            rowElement.style.width = _scrollView.contentViewport.worldBound.width;
            rowElement.style.justifyContent = Justify.SpaceAround;

            return rowElement;
        }

        private VisualElement CreateFillerElement()
        {
            var element = new VisualElement();
            element.style.width = ElementWidth;
            element.style.height = ElementHeight;

            return element;
        }
    }
}