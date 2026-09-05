using System.Collections.Generic;
using System.Linq;
#if UNITY_2021_2_OR_NEWER
using UnityEngine.UIElements;
using UnityEngine;
#else
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
#endif

namespace MCPForUnity.Editor.Windows.Components
{
    /// <summary>
    /// Cross-version dropdown control.
    ///
    /// Unity 2020.3 does not have UnityEngine.UIElements.DropdownField (added in 2021.2)
    /// and its UnityEditor.UIElements.PopupField&lt;string&gt; keeps `choices` private.
    /// On 2020.3 this control self-draws an equivalent dropdown (IMGUI popup) and exposes
    /// the same public surface as DropdownField: choices / index / value /
    /// SetValueWithoutNotify / RegisterValueChangedCallback.
    /// On 2021.2+ this is a plain DropdownField (behaviour identical to upstream).
    ///
    /// Usable from UXML via the nested UxmlFactory (e.g. &lt;mcpx:CompatDropdownField /&gt;).
    /// </summary>
    public class CompatDropdownField :
#if UNITY_2021_2_OR_NEWER
        DropdownField
#else
        VisualElement
#endif
    {
#if UNITY_2021_2_OR_NEWER
        public CompatDropdownField() : base()
        {
        }

        public CompatDropdownField(List<string> choices, int defaultIndex) : base(choices, defaultIndex)
        {
        }

        public new class UxmlFactory : UxmlFactory<CompatDropdownField, UxmlTraits> { }

        public new class UxmlTraits : DropdownField.UxmlTraits
        {
        }
#else
        private readonly List<string> m_Choices = new List<string>();
        private int m_Index = -1;
        private string m_Value;
        private readonly List<EventCallback<ChangeEvent<string>>> m_Callbacks =
            new List<EventCallback<ChangeEvent<string>>>();
        private readonly IMGUIContainer m_Container;

        public CompatDropdownField() : this(new List<string>(), 0)
        {
        }

        public CompatDropdownField(List<string> choices, int defaultIndex)
        {
            m_Container = new IMGUIContainer(OnGui);
            m_Container.style.flexGrow = 1f;
            hierarchy.Add(m_Container);
            m_Choices.AddRange(choices ?? new List<string>());
            index = defaultIndex;
        }

        public List<string> choices
        {
            get { return m_Choices; }
            set
            {
                m_Choices.Clear();
                if (value != null) m_Choices.AddRange(value);
                if (m_Index >= m_Choices.Count) index = m_Choices.Count > 0 ? 0 : -1;
                else UpdateValueFromIndex();
            }
        }

        public int index
        {
            get { return m_Index; }
            set
            {
                int clamped = value;
                if (m_Choices.Count == 0) clamped = -1;
                else if (clamped < 0) clamped = 0;
                else if (clamped >= m_Choices.Count) clamped = m_Choices.Count - 1;
                if (clamped == m_Index) return;
                SetValueWithoutNotify(clamped >= 0 && clamped < m_Choices.Count ? m_Choices[clamped] : null);
            }
        }

        public string value
        {
            get { return m_Value; }
            set
            {
                int newIndex = m_Choices.IndexOf(value);
                if (newIndex >= 0)
                {
                    if (newIndex == m_Index) return;
                    m_Index = newIndex;
                    UpdateValueFromIndex();
                }
            }
        }

        public void SetValueWithoutNotify(string newValue)
        {
            int newIndex = m_Choices.IndexOf(newValue);
            if (newIndex >= 0 && newIndex != m_Index)
            {
                m_Index = newIndex;
                m_Value = newIndex < m_Choices.Count ? m_Choices[newIndex] : null;
            }
        }

        public void RegisterValueChangedCallback(EventCallback<ChangeEvent<string>> callback)
        {
            if (callback != null) m_Callbacks.Add(callback);
        }

        public void UnregisterValueChangedCallback(EventCallback<ChangeEvent<string>> callback)
        {
            m_Callbacks.Remove(callback);
        }

        private void UpdateValueFromIndex()
        {
            string newValue = m_Index >= 0 && m_Index < m_Choices.Count ? m_Choices[m_Index] : null;
            if (newValue == m_Value) return;
            string previous = m_Value;
            m_Value = newValue;
            var evt = ChangeEvent<string>.GetPooled(previous, newValue);
            evt.target = this;
            foreach (var cb in m_Callbacks) cb(evt);
        }

        private void OnGui()
        {
            if (m_Choices.Count == 0)
            {
                EditorGUILayout.Popup(0, new[] { string.Empty });
                return;
            }

            int newIndex = EditorGUILayout.Popup(m_Index < 0 ? 0 : m_Index, m_Choices.ToArray());
            if (newIndex != m_Index)
            {
                index = newIndex;
            }
        }

        public new class UxmlFactory : UxmlFactory<CompatDropdownField, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            private readonly UxmlStringAttributeDescription m_ChoicesAttr =
                new UxmlStringAttributeDescription { name = "choices" };
            private readonly UxmlIntAttributeDescription m_IndexAttr =
                new UxmlIntAttributeDescription { name = "index" };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var field = (CompatDropdownField)ve;
                string choicesStr = m_ChoicesAttr.GetValueFromBag(bag, cc);
                if (!string.IsNullOrEmpty(choicesStr))
                {
                    field.choices = choicesStr.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                }
                int idx = m_IndexAttr.GetValueFromBag(bag, cc);
                if (idx >= 0) field.index = idx;
            }
        }
#endif
    }
}
