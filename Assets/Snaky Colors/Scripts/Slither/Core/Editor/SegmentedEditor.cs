using UnityEditor;
using UnityEngine; 
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.Linq;


namespace SnakyColors
{
    [CustomEditor(typeof(SegmentedCreator))]
    public class SegmentedEditor : Editor
    {
        SegmentedCreator segmentedCreator;

        VisualElement rootUI;

        void OnSceneGUI()
        {
            segmentedCreator = segmentedCreator != null ? segmentedCreator : target as SegmentedCreator;

            if (segmentedCreator.UIPath)
            {
                Handles.color = Color.cyan;
                segmentedCreator.DrawLinesOnEachPointSegment(segmentedCreator.RibPositions, segmentedCreator.preview);

                Handles.color = Color.white;
                if (segmentedCreator.basePathAlgorithm == SlitherPathType.PenStroke)
                    for (int i = 0; i < segmentedCreator.MainPoints.Count; i++)
                    {
                        Handles.DrawSolidDisc(segmentedCreator.MainPoints[i], Vector3.forward, 0.1f);
                    }

                Handles.color = Color.red;
                Handles.DrawSolidDisc(segmentedCreator.wobblingPoint, Vector3.forward, 0.1f);
            }
        }

        public override VisualElement CreateInspectorGUI()
        {
            rootUI = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Snaky Colors/Scripts/Slither/Core/Editor/UI/2DCreaturesUI.uxml").Instantiate();
            rootUI.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Snaky Colors/Scripts/Slither/Core/Editor/UI/UIStyling.uss"));


            SetupPagesForHeaderSelection();

            SetupPageTopToggles();

            // skin scriptable Object Field
            VisualElement spriteSetupPage = rootUI.Q<VisualElement>("SpriteContent");
            VisualElement patternSetupPage = rootUI.Q<VisualElement>("patternPage");
            rootUI.Q<ObjectField>("soObjectField").RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is ScriptableObject soInstance)
                {
                    SetupBindingsForSkinItems();

                    OnMainNavClicked(segmentedCreator.mainNavIndex);
                }
                else
                {
                    spriteSetupPage.style.display = DisplayStyle.None;
                    patternSetupPage.style.display = DisplayStyle.None;
                }
            });

            AddSOButton(); // add button for scriptable object (Skin)


            SetupPagesForPatternSelection();

            SetupCustomPrefabPage();


            RefreshCreatureForTweaks();


            // item inside movement page
            var movementPage = rootUI.Q<TemplateContainer>("MovementPage");

            VisualElement wobblePage = movementPage.Q<VisualElement>("WobbleProp");
            movementPage.Q<Toggle>("moveThruTarget").RegisterValueChangedCallback((evt) => wobblePage.SetEnabled(!evt.newValue));


            // render sorting slider callback
            rootUI.Q<SliderInt>("RibCountSlider").RegisterValueChangedCallback((evt) => UpdateRenderSortingSliderRange());


            return rootUI;
        }


        // setting render sorting slider
        public void UpdateRenderSortingSliderRange()
        {
            var renderSortingSlider = rootUI.Q<SliderInt>("sortingOrder");

            renderSortingSlider.RegisterValueChangedCallback(evt =>
            {
                int ribCount = Mathf.Max(1, segmentedCreator.ribCount);

                int rangeSpread = 500;

                int lowVal = !segmentedCreator.spritesOrderinverted
                    ? (-rangeSpread + ribCount) / ribCount
                    : (-rangeSpread - ribCount) / ribCount;

                int highVal = segmentedCreator.spritesOrderinverted
                    ? (rangeSpread + ribCount) / ribCount
                    : (rangeSpread - ribCount) / ribCount;

                renderSortingSlider.lowValue = Mathf.Min(lowVal, highVal);
                renderSortingSlider.highValue = Mathf.Max(lowVal, highVal);
            });
        }


        // custom prefab page setup
        public void SetupCustomPrefabPage()
        {
            ListView customPrefabList = rootUI.Q<ListView>("customPrefabsList");
            VisualElement propertyPage = rootUI.Q<VisualElement>("PrefabProperty");

            SerializedProperty spriteOverridesProp = serializedObject.FindProperty("spriteOverrides");

            VisualTreeAsset listEntryTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Snaky Colors/Scripts/Slither/Core/Editor/UI/ListEntry.uxml");
            VisualTreeAsset propertyTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Snaky Colors/Scripts/Slither/Core/Editor/UI/customPrefabProp.uxml");

            if (listEntryTemplate == null || propertyTemplate == null)
            {
                return;
            }

            customPrefabList.BindProperty(spriteOverridesProp);

            customPrefabList.makeItem = () => listEntryTemplate.Instantiate();
            customPrefabList.bindItem = (e, i) =>
            {
                SerializedProperty prefabProp = spriteOverridesProp.GetArrayElementAtIndex(i).FindPropertyRelative("prefab");
                ObjectField field = e.Q<ObjectField>("prefabObjField");

                field.label = $"Prefab: {i}";
                field.BindProperty(prefabProp);
            };

            customPrefabList.selectionChanged += (selItems) =>
            {
                SetupCustomPrefabProperties();
            };

            customPrefabList.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (customPrefabList.selectedIndex != -1)  // Ensure something is selected
                {
                    SetupCustomPrefabProperties();
                }
            });

            void SetupCustomPrefabProperties()
            {
                propertyPage.Clear();
                int i = customPrefabList.selectedIndex;

                if (i < 0 || i >= segmentedCreator.spriteOverrides.Count) return;

                VisualElement prop = propertyTemplate.Instantiate();
                propertyPage.Add(prop);

                Transform prefabObj = segmentedCreator.spriteOverrides[i].prefab;
                prop.Q<Label>("propertyLabel").text = prefabObj ? $"Prefab Properties: {prefabObj.name}" : "Prefab Properties";

                SerializedProperty positionProp = spriteOverridesProp.GetArrayElementAtIndex(i).FindPropertyRelative("Position");
                SliderInt posSlider = prop.Q<SliderInt>("posSlider");
                posSlider.highValue = segmentedCreator.RibPositions.Count - 1;
                posSlider.BindProperty(positionProp);

                VisualElement layerOrderContainer = prop.Q<VisualElement>("layerOrderContainer");
                layerOrderContainer.Clear();

                if (prefabObj != null)
                {
                    foreach (SpriteRenderer renderer in prefabObj.GetComponentsInChildren<SpriteRenderer>())
                    {
                        int ribCount = segmentedCreator.ribCount - 1;

                        int LayersOffset = segmentedCreator.orderInLayer * ribCount;
                        int currentOrderInLayer = segmentedCreator.spritesOrderinverted ? LayersOffset + ribCount : LayersOffset - ribCount;

                        int lowVal = LayersOffset;
                        int highVal = segmentedCreator.spritesOrderinverted
                            ? LayersOffset + ribCount : LayersOffset - ribCount;

                        SliderInt layerOrderSlider = new($"Layer Order: {renderer.gameObject.name}")
                        {
                            value = renderer.sharedMaterial != null ? renderer.sharedMaterial.renderQueue - 3000 : 0,
                            highValue = highVal,
                            lowValue = lowVal,
                            showInputField = true
                        };

                        Material clonedMat = new(renderer.sharedMaterial)
                        {
                            name = renderer.sharedMaterial.name + "_Cloned"
                        };
                        renderer.sharedMaterial = clonedMat;

#if UNITY_EDITOR
                        Undo.RecordObject(renderer, "Assign Cloned Material");
                        EditorUtility.SetDirty(renderer);
#endif

                        layerOrderSlider.RegisterValueChangedCallback(evt =>
                        {
                            if (renderer.sharedMaterial != null)
                            {
                                renderer.sharedMaterial.renderQueue = 3000 + evt.newValue;
                            }
                        });

                        layerOrderContainer.Add(layerOrderSlider);
                    }

                }
            }

        }


        // items need preview pane update
        public void RefreshCreatureForTweaks()
        {
            rootUI.Q<EnumField>("skinStripeType").RegisterValueChangedCallback((evt) => { segmentedCreator.RefreshSprites(); });
            rootUI.Q<SliderInt>("stripeCount").RegisterValueChangedCallback((evt) => { segmentedCreator.RefreshSprites(); });

            rootUI.Q<Toggle>("flipConseToggle").RegisterValueChangedCallback((evt) => { segmentedCreator.RefreshSprites(); });

            rootUI.Q<ObjectField>("repeatedStripeSpriteField").RegisterValueChangedCallback((evt) => { segmentedCreator.RefreshSprites(); });
            rootUI.Q<PropertyField>("CustomStripeSprite").RegisterValueChangeCallback((evt) => { segmentedCreator.RefreshSprites(); });


            rootUI.Q<Slider>("rotOverride").RegisterValueChangedCallback((evt) => { segmentedCreator.RefreshSprites(); });

            rootUI.Q<ObjectField>("headField").RegisterValueChangedCallback((evt) => { segmentedCreator.RefreshSprites(); });
            rootUI.Q<ObjectField>("bodyField").RegisterValueChangedCallback((evt) => { segmentedCreator.RefreshSprites(); });

            rootUI.Q<ObjectField>("tailField").RegisterValueChangedCallback((evt) => { segmentedCreator.RefreshSprites(); });
            rootUI.Q<Toggle>("useTailToggle").RegisterValueChangedCallback((evt) => { segmentedCreator.RefreshSprites(); });

        }


        // stripe type change callbacks
        public void OnPatternChange(StripeType stripeType)
        {
            string[] allPatternPages = { "patternControls", "noneStripe", "repeatStripe", "CustomStripe" };
            foreach (string element in allPatternPages)
            {
                rootUI.Q<VisualElement>(element).style.display = DisplayStyle.None;
            }

            switch (stripeType)
            {
                case StripeType.None:
                    ShowElements("noneStripe");
                    break;
                case StripeType.Repeat:
                    ShowElements("patternControls");
                    ShowElements("repeatStripe");
                    break;
                case StripeType.Custom:
                    ShowElements("patternControls");
                    ShowElements("CustomStripe");
                    break;
            }
        }

        public void SetupPagesForPatternSelection()
        {
            rootUI.Q<EnumField>("skinStripeType").RegisterValueChangedCallback((evt) =>
            {
                OnPatternChange((StripeType)evt.newValue);
            });
        }


        // skin SO relative items
        public void AddSOButton()
        {
            rootUI.Q<Button>("addSOButton").clicked += () =>
            {
                Skin skinSO = CreateInstance<Skin>();
                segmentedCreator.skin = skinSO;

                string baseName = "Assets/New_Skin";
                string assetPath = $"{baseName}.asset";
                int counter = 1;

                while (AssetDatabase.LoadAssetAtPath<Skin>(assetPath) != null)
                {
                    assetPath = $"{baseName} ({counter}).asset";
                    counter++;
                }

                AssetDatabase.CreateAsset(skinSO, assetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log("Skin SO created in " + AssetDatabase.GetAssetPath(skinSO));
            };
        }

        public void SetupBindingsForSkinItems()
        {
            SerializedProperty skinProperty = serializedObject.FindProperty("skin");

            if (skinProperty?.objectReferenceValue is Skin skin)
            {
                SerializedObject skinSO = new(skin);

                // sprite page bindings
                rootUI.Q<ObjectField>("headField").BindProperty(skinSO.FindProperty("HeadSprite"));
                rootUI.Q<ObjectField>("bodyField").BindProperty(skinSO.FindProperty("BodySprite"));

                rootUI.Q<Toggle>("useTailToggle").BindProperty(skinSO.FindProperty("useTail"));
                rootUI.Q<ObjectField>("tailField").BindProperty(skinSO.FindProperty("TailSprite"));

                rootUI.Q<ObjectField>("skinMatField").BindProperty(skinSO.FindProperty("mat"));

                rootUI.Q<Slider>("rotOverride").BindProperty(skinSO.FindProperty("EachSpriteRotAngle"));

                // pattern page bindings
                rootUI.Q<EnumField>("skinStripeType").BindProperty(skinSO.FindProperty("currentStripeType"));

                rootUI.Q<SliderInt>("stripeCount").BindProperty(skinSO.FindProperty("stripesCount"));

                rootUI.Q<SliderInt>("beforeStripeSpacing").BindProperty(skinSO.FindProperty("stripesSpacingBeforeStripe"));
                rootUI.Q<SliderInt>("AfterStripeSpacing").BindProperty(skinSO.FindProperty("stripesSpacingAfterStripe"));

                rootUI.Q<Toggle>("flipConseToggle").BindProperty(skinSO.FindProperty("FlipConsecutive"));

                SerializedProperty repeatedSprites = skinSO.FindProperty("repeatedStripe").FindPropertyRelative("sprite");
                rootUI.Q<ObjectField>("repeatedStripeSpriteField").BindProperty(repeatedSprites);

                SerializedProperty repeatedSpritesLength = skinSO.FindProperty("repeatedStripe").FindPropertyRelative("stripeLength");
                rootUI.Q<SliderInt>("repeatedStripeLength").BindProperty(repeatedSpritesLength);

                SerializedProperty customSprites = skinSO.FindProperty("customStripe").FindPropertyRelative("sprites");
                rootUI.Q<PropertyField>("CustomStripeSprite").BindProperty(customSprites);
            }
        }


        // page top toggles
        public void SetupPageTopToggles()
        {
            PageTopButton("movementPageToggle", "movementItems");

            PageTopButton("setupControlsToggle", "basePathProp");

            PageTopButton("spriteToggle", "SpriteSetupItems");

            PageTopButton("PatternToggle", "patternPageItems");

            PageTopButton("CustomPrefabToggle", "CustomPrefabPageItems");

            void PageTopButton(string ToggleButton, string elementName)
            {
                rootUI.Q<ToolbarToggle>(ToggleButton).RegisterValueChangedCallback((evt) =>
                {
                    rootUI.Q<VisualElement>(elementName).style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                });
            }
        }


        // header navigation and refresh button
        public void SetupPagesForHeaderSelection()
        {
            // main nav toggles
            VisualElement mainNavDep = rootUI.Q("mainNavDep");

            rootUI.Q<ToolbarButton>("refreshButton").clicked += () =>
            {
                segmentedCreator.RefreshSprites();
            };

            void DepSelBehaviour(int index)
            {
                foreach (ToolbarToggle toggle in mainNavDep.Children().Cast<ToolbarToggle>())
                {
                    toggle.SetValueWithoutNotify(false);

                    if (mainNavDep.IndexOf(toggle) == index)
                    {
                        toggle.SetValueWithoutNotify(true);
                    }
                }
            }

            OnMainNavClicked(segmentedCreator.mainNavIndex);
            DepSelBehaviour(segmentedCreator.mainNavIndex);

            foreach (var child in mainNavDep.Children())
            {
                if (child is ToolbarToggle toggle)
                {
                    toggle.RegisterValueChangedCallback(evt =>
                    {
                        DepSelBehaviour(mainNavDep.Children().ToList().IndexOf(toggle));
                        OnMainNavClicked(mainNavDep.Children().ToList().IndexOf(toggle));  // Calling additional code on clicks
                    });
                }
            }
        }


        public void OnMainNavClicked(int index)
        {
            segmentedCreator.mainNavIndex = index;

            string[] allElements = { "subHeader", "skinProp", "skinSO", "SpriteContent", "patternPage", "CustomPrefab", "MovementPage" };
            foreach (string element in allElements)
            {
                rootUI.Q<VisualElement>(element).style.display = DisplayStyle.None;
            }

            switch (index)
            {
                case 0:
                    ShowElements("subHeader", "skinProp", "skinSO", segmentedCreator.skin != null ? "SpriteContent" : null);
                    break;
                case 1:
                    ShowElements("subHeader", "skinProp", "skinSO", segmentedCreator.skin != null ? "patternPage" : null);

                    if (segmentedCreator.skin != null)
                        OnPatternChange(segmentedCreator.skin.currentStripeType);
                    break;
                case 2:
                    ShowElements("skinProp", "CustomPrefab");
                    break;
                case 3:
                    ShowElements("MovementPage");
                    break;
            }

        }

        // helper method to show elements
        public void ShowElements(params string[] elements)
        {
            foreach (string element in elements)
            {
                rootUI.Q<VisualElement>(element).style.display = DisplayStyle.Flex;
            }
        }


        public void OnEnable()
        {
            segmentedCreator = target as SegmentedCreator;
        }
    }
}
