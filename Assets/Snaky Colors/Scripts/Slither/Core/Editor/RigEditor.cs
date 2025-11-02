using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace SnakyColors
{
    [CustomEditor(typeof(RigAnimator))]
    public class RigEditor : Editor
    {
        public RigAnimator rigAnimator;

        VisualElement rootUI;

        public override VisualElement CreateInspectorGUI()
        {
            rootUI = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Snaky Colors/Scripts/Slither/Core/Editor/UI/RiggedCreatureUI.uxml").Instantiate();

            rootUI.Q<ToolbarToggle>("movementPageToggle").RegisterValueChangedCallback((evt) =>
            {
                rootUI.Q<VisualElement>("movementItems").style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
            });

            rootUI.Q<ToolbarToggle>("setupPageToggle").RegisterValueChangedCallback((evt) =>
            {
                rootUI.Q<VisualElement>("setupItems").style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
            });


            // item inside movement page
            var movementPage = rootUI.Q<TemplateContainer>("MovementPage");

            VisualElement wobblePage = movementPage.Q<VisualElement>("WobbleProp");
            movementPage.Q<Toggle>("moveThruTarget").RegisterValueChangedCallback((evt) => wobblePage.SetEnabled(!evt.newValue));


            return rootUI;
        }
    }
}
