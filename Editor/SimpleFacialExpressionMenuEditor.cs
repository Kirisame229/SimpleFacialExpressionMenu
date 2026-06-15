using UnityEditor;
using UnityEngine;
using SimpleFacialExpressionMenuTool;
using System.Collections.Generic;
using UnityEditorInternal;
using VRC.SDK3.Avatars.Components;

namespace SimpleFacialExpressionMenuTool.Editor
{
    [CustomEditor(typeof(SimpleFacialExpressionMenu))]
    internal sealed class SimpleFacialExpressionMenuEditor : UnityEditor.Editor
    {
        private const float LabelWidth = 128f;
        private const float ResetButtonWidth = 56f;
        private const float FieldSpacing = 0f;
        private const int PreviewDisplaySize = 96;
        private const int PreviewRenderSize = 256;
        private const int PreviewLayer = 31;
        private const string AdvancedOptionsFoldoutKeyPrefix = "me.kirisame.sfem.advancedOptionsFoldout.";
        private const string ThumbnailCameraFoldoutKeyPrefix = "me.kirisame.sfem.thumbnailCameraFoldout.";
        private static readonly string[] LanguageDisplayNames = { "日本語", "한국어", "English" };

        private SerializedProperty _rootMenuIcon;
        private SerializedProperty _rootMenuName;
        private SerializedProperty _installTargetMenu;
        private SerializedProperty _pages;
        private SerializedProperty _thumbnailCameraOffsetX;
        private SerializedProperty _thumbnailCameraOffsetY;
        private SerializedProperty _thumbnailCameraZoom;
        private SerializedProperty _thumbnailPadding;
        private SerializedProperty _layerPriority;
        private SerializedProperty _writeDefaults;
        private SerializedProperty _language;
        private readonly Dictionary<string, ReorderableList> _slotLists = new Dictionary<string, ReorderableList>();
        private bool _showAdvancedOptions;
        private bool _showThumbnailCameraOptions;
        private GameObject _previewRoot;
        private GameObject _previewCameraObject;
        private Camera _previewCamera;
        private RenderTexture _previewRenderTexture;
        private Texture2D _previewTexture;
        private VRCAvatarDescriptor _previewSourceDescriptor;

        private void OnEnable()
        {
            _rootMenuIcon = serializedObject.FindProperty(nameof(SimpleFacialExpressionMenu.rootMenuIcon));
            _rootMenuName = serializedObject.FindProperty(nameof(SimpleFacialExpressionMenu.rootMenuName));
            _installTargetMenu = serializedObject.FindProperty(nameof(SimpleFacialExpressionMenu.installTargetMenu));
            _pages = serializedObject.FindProperty(nameof(SimpleFacialExpressionMenu.pages));
            _thumbnailCameraOffsetX = serializedObject.FindProperty(nameof(SimpleFacialExpressionMenu.thumbnailCameraOffsetX));
            _thumbnailCameraOffsetY = serializedObject.FindProperty(nameof(SimpleFacialExpressionMenu.thumbnailCameraOffsetY));
            _thumbnailCameraZoom = serializedObject.FindProperty(nameof(SimpleFacialExpressionMenu.thumbnailCameraZoom));
            _thumbnailPadding = serializedObject.FindProperty(nameof(SimpleFacialExpressionMenu.thumbnailPadding));
            _layerPriority = serializedObject.FindProperty(nameof(SimpleFacialExpressionMenu.layerPriority));
            _writeDefaults = serializedObject.FindProperty(nameof(SimpleFacialExpressionMenu.writeDefaults));
            _language = serializedObject.FindProperty(nameof(SimpleFacialExpressionMenu.language));
            _showAdvancedOptions = EditorPrefs.GetBool(AdvancedOptionsFoldoutKey(), false);
            _showThumbnailCameraOptions = EditorPrefs.GetBool(ThumbnailCameraFoldoutKey(), false);
        }

        private void OnDisable()
        {
            CleanupPreviewResources();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var language = (SimpleFacialExpressionLanguage)_language.enumValueIndex;
            var component = (SimpleFacialExpressionMenu)target;

            if (!TryGetValidAvatarDescriptor(component, out _))
            {
                CleanupPreviewResources();

                EditorGUILayout.HelpBox(
                    SimpleFacialExpressionLocalization.Text(language, "invalidAvatarDescriptor"),
                    MessageType.Warning);

                EditorGUILayout.Space(8);
                DrawLanguage(language);

                serializedObject.ApplyModifiedProperties();
                component.EnsureValidState();
                return;
            }

            DrawMenuSettings(language);

            EditorGUILayout.Space(8);
            DrawPages(language);

            EditorGUILayout.Space(8);
            DrawThumbnailCameraOptions(language);

            EditorGUILayout.Space(8);
            DrawAdvancedOptions(language);

            EditorGUILayout.Space(8);
            DrawLanguage(language);

            serializedObject.ApplyModifiedProperties();

            component.EnsureValidState();
        }

        private void DrawMenuSettings(SimpleFacialExpressionLanguage language)
        {
            DrawProperty(_rootMenuIcon, SimpleFacialExpressionLocalization.Label(language, "rootIcon"));
            DrawProperty(_rootMenuName, SimpleFacialExpressionLocalization.Label(language, "rootName"));
            DrawProperty(_installTargetMenu, SimpleFacialExpressionLocalization.Label(language, "installTarget"));
        }

        private void DrawThumbnailCameraOptions(SimpleFacialExpressionLanguage language)
        {
            var component = (SimpleFacialExpressionMenu)target;
            var previousShowThumbnailCameraOptions = _showThumbnailCameraOptions;
            _showThumbnailCameraOptions = EditorGUILayout.Foldout(
                _showThumbnailCameraOptions,
                SimpleFacialExpressionLocalization.Text(language, "thumbnailCamera"),
                true);
            if (_showThumbnailCameraOptions != previousShowThumbnailCameraOptions)
            {
                EditorPrefs.SetBool(ThumbnailCameraFoldoutKey(), _showThumbnailCameraOptions);
            }

            if (!_showThumbnailCameraOptions)
            {
                CleanupPreviewResources();
                return;
            }

            EditorGUI.indentLevel++;
            using (new EditorGUILayout.HorizontalScope())
            {
                var previewRect = GUILayoutUtility.GetRect(
                    PreviewDisplaySize,
                    PreviewDisplaySize,
                    GUILayout.Width(PreviewDisplaySize),
                    GUILayout.Height(PreviewDisplaySize));
                DrawThumbnailPreview(component, previewRect);

                using (new EditorGUILayout.VerticalScope())
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(
                                new GUIContent(
                                    SimpleFacialExpressionLocalization.Text(language, "resetToDefault"),
                                    SimpleFacialExpressionLocalization.Text(language, "resetToDefault")),
                                EditorStyles.miniButton,
                                GUILayout.Width(ResetButtonWidth)))
                        {
                            _thumbnailCameraOffsetX.floatValue = SimpleFacialExpressionMenu.DefaultThumbnailCameraOffsetX;
                            _thumbnailCameraOffsetY.floatValue = SimpleFacialExpressionMenu.DefaultThumbnailCameraOffsetY;
                            _thumbnailCameraZoom.floatValue = SimpleFacialExpressionMenu.DefaultThumbnailCameraZoom;
                            _thumbnailPadding.floatValue = SimpleFacialExpressionMenu.DefaultThumbnailPadding;
                            Repaint();
                        }
                    }

                    EditorGUI.BeginChangeCheck();
                    DrawSlider(
                        _thumbnailCameraOffsetX,
                        SimpleFacialExpressionLocalization.Label(language, "thumbnailCameraOffsetX"),
                        SimpleFacialExpressionMenu.MinThumbnailCameraOffset,
                        SimpleFacialExpressionMenu.MaxThumbnailCameraOffset);
                    DrawSlider(
                        _thumbnailCameraOffsetY,
                        SimpleFacialExpressionLocalization.Label(language, "thumbnailCameraOffsetY"),
                        SimpleFacialExpressionMenu.MinThumbnailCameraOffset,
                        SimpleFacialExpressionMenu.MaxThumbnailCameraOffset);
                    DrawSlider(
                        _thumbnailCameraZoom,
                        SimpleFacialExpressionLocalization.Label(language, "thumbnailCameraZoom"),
                        SimpleFacialExpressionMenu.MinThumbnailCameraZoom,
                        SimpleFacialExpressionMenu.MaxThumbnailCameraZoom);
                    DrawSlider(
                        _thumbnailPadding,
                        SimpleFacialExpressionLocalization.Label(language, "thumbnailPadding"),
                        SimpleFacialExpressionMenu.MinThumbnailPadding,
                        SimpleFacialExpressionMenu.MaxThumbnailPadding);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Repaint();
                    }
                }
            }
            EditorGUI.indentLevel--;
        }

        private void DrawAdvancedOptions(SimpleFacialExpressionLanguage language)
        {
            var previousShowAdvancedOptions = _showAdvancedOptions;
            _showAdvancedOptions = EditorGUILayout.Foldout(
                _showAdvancedOptions,
                SimpleFacialExpressionLocalization.Text(language, "advancedOptions"),
                true);
            if (_showAdvancedOptions != previousShowAdvancedOptions)
            {
                EditorPrefs.SetBool(AdvancedOptionsFoldoutKey(), _showAdvancedOptions);
            }

            if (!_showAdvancedOptions)
            {
                return;
            }

            EditorGUI.indentLevel++;
            DrawProperty(
                _layerPriority,
                SimpleFacialExpressionLocalization.Label(language, "layerPriority"));
            DrawProperty(
                _writeDefaults,
                SimpleFacialExpressionLocalization.Label(language, "writeDefaults"));
            EditorGUI.indentLevel--;
        }

        private string AdvancedOptionsFoldoutKey()
        {
            if (target == null)
            {
                return AdvancedOptionsFoldoutKeyPrefix + "unknown";
            }

            var globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(target).ToString();
            if (string.IsNullOrEmpty(globalObjectId))
            {
                globalObjectId = target.GetInstanceID().ToString();
            }

            return AdvancedOptionsFoldoutKeyPrefix + globalObjectId;
        }

        private string ThumbnailCameraFoldoutKey()
        {
            if (target == null)
            {
                return ThumbnailCameraFoldoutKeyPrefix + "unknown";
            }

            var globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(target).ToString();
            if (string.IsNullOrEmpty(globalObjectId))
            {
                globalObjectId = target.GetInstanceID().ToString();
            }

            return ThumbnailCameraFoldoutKeyPrefix + globalObjectId;
        }

        private void DrawThumbnailPreview(SimpleFacialExpressionMenu component, Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f, 1f));

            if (Event.current.type == EventType.Repaint)
            {
                RenderThumbnailPreview(component);
            }

            if (_previewTexture != null)
            {
                GUI.DrawTexture(rect, _previewTexture, ScaleMode.ScaleToFit, true);
            }
        }

        private void RenderThumbnailPreview(SimpleFacialExpressionMenu component)
        {
            if (!EnsurePreviewResources(component))
            {
                return;
            }

            var previousRenderTexture = RenderTexture.active;
            try
            {
                SetPreviewRenderersEnabled(true);
                CopyPreviewBlendShapeWeights();
                CalculatePreviewCamera(
                    _previewRoot,
                    _thumbnailCameraOffsetX.floatValue,
                    _thumbnailCameraOffsetY.floatValue,
                    _thumbnailCameraZoom.floatValue,
                    out var cameraPosition,
                    out var cameraRotation,
                    out var orthographicSize,
                    out var farClip);

                _previewCamera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                _previewCamera.orthographicSize = orthographicSize;
                _previewCamera.farClipPlane = farClip;

                _previewCamera.Render();
                RenderTexture.active = _previewRenderTexture;
                _previewTexture.ReadPixels(new Rect(0, 0, PreviewRenderSize, PreviewRenderSize), 0, 0);
                ApplyThumbnailPaddingAndCircularMask(_previewTexture, _thumbnailPadding.floatValue);
            }
            finally
            {
                SetPreviewRenderersEnabled(false);
                RenderTexture.active = previousRenderTexture;
            }
        }

        private bool EnsurePreviewResources(SimpleFacialExpressionMenu component)
        {
            if (!TryGetValidAvatarDescriptor(component, out var avatarDescriptor))
            {
                CleanupPreviewResources();
                return false;
            }

            if (_previewRoot != null && _previewSourceDescriptor == avatarDescriptor)
            {
                return true;
            }

            CleanupPreviewResources();
            _previewSourceDescriptor = avatarDescriptor;

            _previewRoot = UnityEngine.Object.Instantiate(avatarDescriptor.gameObject);
            _previewRoot.name = "__SFEM_InspectorPreview_" + component.GetInstanceID();
            _previewRoot.hideFlags = HideFlags.HideAndDontSave;
            _previewRoot.SetActive(true);
            SetHideFlagsRecursively(_previewRoot.transform, HideFlags.HideAndDontSave);
            DisablePreviewBehaviours(_previewRoot);
            SetLayerRecursively(_previewRoot.transform, PreviewLayer);
            SetPreviewRenderersEnabled(false);

            _previewRenderTexture = new RenderTexture(PreviewRenderSize, PreviewRenderSize, 24, RenderTextureFormat.ARGB32)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _previewTexture = new Texture2D(PreviewRenderSize, PreviewRenderSize, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            _previewCameraObject = new GameObject("__SFEM_InspectorPreviewCamera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _previewCamera = _previewCameraObject.AddComponent<Camera>();
            _previewCamera.clearFlags = CameraClearFlags.SolidColor;
            _previewCamera.backgroundColor = new Color(0, 0, 0, 0);
            _previewCamera.cullingMask = 1 << PreviewLayer;
            _previewCamera.orthographic = true;
            _previewCamera.nearClipPlane = 0.01f;
            _previewCamera.allowHDR = false;
            _previewCamera.targetTexture = _previewRenderTexture;

            return true;
        }

        private void CleanupPreviewResources()
        {
            if (_previewCamera != null)
            {
                _previewCamera.targetTexture = null;
            }

            if (_previewRenderTexture != null)
            {
                _previewRenderTexture.Release();
                UnityEngine.Object.DestroyImmediate(_previewRenderTexture);
            }

            if (_previewTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(_previewTexture);
            }

            if (_previewCameraObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_previewCameraObject);
            }

            if (_previewRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(_previewRoot);
            }

            _previewRoot = null;
            _previewCameraObject = null;
            _previewCamera = null;
            _previewRenderTexture = null;
            _previewTexture = null;
            _previewSourceDescriptor = null;
        }

        private void CopyPreviewBlendShapeWeights()
        {
            if (_previewSourceDescriptor == null || _previewRoot == null)
            {
                return;
            }

            var sourceRoot = _previewSourceDescriptor.gameObject;
            foreach (var sourceRenderer in sourceRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var sourceMesh = sourceRenderer.sharedMesh;
                if (sourceMesh == null)
                {
                    continue;
                }

                var path = AnimationUtility.CalculateTransformPath(sourceRenderer.transform, sourceRoot.transform);
                var targetTransform = FindTransformByPath(_previewRoot.transform, path);
                if (targetTransform == null || !targetTransform.TryGetComponent<SkinnedMeshRenderer>(out var targetRenderer))
                {
                    continue;
                }

                var targetMesh = targetRenderer.sharedMesh;
                if (targetMesh == null)
                {
                    continue;
                }

                var count = Mathf.Min(sourceMesh.blendShapeCount, targetMesh.blendShapeCount);
                for (var shapeIndex = 0; shapeIndex < count; shapeIndex++)
                {
                    targetRenderer.SetBlendShapeWeight(shapeIndex, sourceRenderer.GetBlendShapeWeight(shapeIndex));
                }
            }
        }

        private void SetPreviewRenderersEnabled(bool enabled)
        {
            if (_previewRoot == null)
            {
                return;
            }

            foreach (var renderer in _previewRoot.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = enabled && IsSourceRendererEnabled(renderer);
            }
        }

        private bool IsSourceRendererEnabled(Renderer previewRenderer)
        {
            if (_previewSourceDescriptor == null)
            {
                return true;
            }

            var sourceRoot = _previewSourceDescriptor.gameObject;
            var path = AnimationUtility.CalculateTransformPath(previewRenderer.transform, _previewRoot.transform);
            var sourceTransform = FindTransformByPath(sourceRoot.transform, path);
            if (sourceTransform == null || !sourceTransform.TryGetComponent<Renderer>(out var sourceRenderer))
            {
                return true;
            }

            return sourceRenderer.enabled;
        }

        private static void CalculatePreviewCamera(
            GameObject previewRoot,
            float offsetX,
            float offsetY,
            float zoom,
            out Vector3 cameraPosition,
            out Quaternion cameraRotation,
            out float orthographicSize,
            out float farClip)
        {
            var rootTransform = previewRoot.transform;
            var bounds = CalculateRendererBounds(previewRoot);
            var avatarHeight = Mathf.Max(bounds.size.y, 1.2f);
            var forward = rootTransform.forward.sqrMagnitude > 0.0001f ? rootTransform.forward.normalized : Vector3.forward;
            var up = rootTransform.up.sqrMagnitude > 0.0001f ? rootTransform.up.normalized : Vector3.up;
            var right = rootTransform.right.sqrMagnitude > 0.0001f ? rootTransform.right.normalized : Vector3.right;
            var avatarDescriptor = previewRoot.GetComponent<VRCAvatarDescriptor>();
            var referenceHeight = avatarHeight;
            var viewPosition = avatarDescriptor != null
                ? rootTransform.TransformPoint(avatarDescriptor.ViewPosition)
                : bounds.center + up * (avatarHeight * 0.38f);
            if (avatarDescriptor != null)
            {
                var viewHeight = Mathf.Abs(Vector3.Dot(viewPosition - rootTransform.position, up));
                if (viewHeight > 0.001f)
                {
                    referenceHeight = Mathf.Min(avatarHeight, viewHeight * 1.25f);
                }
            }

            var target = viewPosition + forward * (avatarHeight * 0.02f);
            var distance = Mathf.Clamp(avatarHeight * 0.22f, 0.25f, 0.8f);
            var clampedZoom = Mathf.Clamp(
                zoom,
                SimpleFacialExpressionMenu.MinThumbnailCameraZoom,
                SimpleFacialExpressionMenu.MaxThumbnailCameraZoom);
            var baseOrthographicSize = Mathf.Clamp(referenceHeight * 0.05f, 0.035f, 0.12f);

            target += right * (avatarHeight * offsetX);
            target += up * (avatarHeight * offsetY);
            cameraPosition = target + forward * distance;
            cameraRotation = Quaternion.LookRotation(target - cameraPosition, up);
            orthographicSize = Mathf.Max(baseOrthographicSize / clampedZoom, 0.005f);
            farClip = distance + avatarHeight * 2f;
        }

        private static Bounds CalculateRendererBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var hasBounds = false;
            var bounds = new Bounds(root.transform.position, Vector3.one);
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return bounds;
        }

        private static Transform FindTransformByPath(Transform root, string path)
        {
            return string.IsNullOrEmpty(path) ? root : root.Find(path);
        }

        private static void DisablePreviewBehaviours(GameObject root)
        {
            foreach (var behaviour in root.GetComponentsInChildren<Behaviour>(true))
            {
                behaviour.enabled = false;
            }
        }

        private static void SetHideFlagsRecursively(Transform transform, HideFlags hideFlags)
        {
            transform.gameObject.hideFlags = hideFlags;
            for (var childIndex = 0; childIndex < transform.childCount; childIndex++)
            {
                SetHideFlagsRecursively(transform.GetChild(childIndex), hideFlags);
            }
        }

        private static void SetLayerRecursively(Transform transform, int layer)
        {
            transform.gameObject.layer = layer;
            for (var childIndex = 0; childIndex < transform.childCount; childIndex++)
            {
                SetLayerRecursively(transform.GetChild(childIndex), layer);
            }
        }

        private static void ApplyThumbnailPaddingAndCircularMask(Texture2D texture, float padding)
        {
            var width = texture.width;
            var height = texture.height;
            var sourcePixels = texture.GetPixels();
            var outputPixels = new Color[sourcePixels.Length];
            var centerX = (width - 1) * 0.5f;
            var centerY = (height - 1) * 0.5f;
            var radius = Mathf.Min(width, height) * 0.5f;
            var softEdge = 2f;
            var scale = 1f - Mathf.Clamp(
                padding,
                SimpleFacialExpressionMenu.MinThumbnailPadding,
                SimpleFacialExpressionMenu.MaxThumbnailPadding);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var sourceX = centerX + (x - centerX) / scale;
                    var sourceY = centerY + (y - centerY) / scale;
                    if (sourceX < 0 || sourceX > width - 1 || sourceY < 0 || sourceY > height - 1)
                    {
                        continue;
                    }

                    var color = SampleBilinear(sourcePixels, width, height, sourceX, sourceY);
                    var distance = Vector2.Distance(
                        new Vector2(sourceX, sourceY),
                        new Vector2(centerX, centerY));
                    var alpha = Mathf.Clamp01((radius - distance) / softEdge);
                    color.a *= alpha;
                    outputPixels[y * width + x] = color;
                }
            }

            texture.SetPixels(outputPixels);
            texture.Apply();
        }

        private static Color SampleBilinear(Color[] pixels, int width, int height, float x, float y)
        {
            var x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, width - 1);
            var y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, height - 1);
            var x1 = Mathf.Min(x0 + 1, width - 1);
            var y1 = Mathf.Min(y0 + 1, height - 1);
            var tx = x - x0;
            var ty = y - y0;

            var bottom = Color.Lerp(pixels[y0 * width + x0], pixels[y0 * width + x1], tx);
            var top = Color.Lerp(pixels[y1 * width + x0], pixels[y1 * width + x1], tx);
            return Color.Lerp(bottom, top, ty);
        }

        private void DrawLanguage(SimpleFacialExpressionLanguage language)
        {
            var previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = LabelWidth;
            _language.enumValueIndex = EditorGUILayout.Popup(
                SimpleFacialExpressionLocalization.Label(language, "language"),
                _language.enumValueIndex,
                LanguageDisplayNames);
            EditorGUIUtility.labelWidth = previousLabelWidth;
            EditorGUILayout.Space(FieldSpacing);
        }

        private static void DrawProperty(SerializedProperty property, GUIContent label)
        {
            var previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = LabelWidth;
            EditorGUILayout.PropertyField(property, label);
            EditorGUIUtility.labelWidth = previousLabelWidth;
            EditorGUILayout.Space(FieldSpacing);
        }

        private static void DrawSlider(SerializedProperty property, GUIContent label, float min, float max)
        {
            var previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = LabelWidth;
            property.floatValue = EditorGUILayout.Slider(label, property.floatValue, min, max);
            EditorGUIUtility.labelWidth = previousLabelWidth;
            EditorGUILayout.Space(FieldSpacing);
        }

        private static bool TryGetValidAvatarDescriptor(
            SimpleFacialExpressionMenu component,
            out VRCAvatarDescriptor avatarDescriptor)
        {
            avatarDescriptor = component.GetComponentInParent<VRCAvatarDescriptor>(true);
            return avatarDescriptor != null
                   && avatarDescriptor.customExpressions
                   && avatarDescriptor.expressionsMenu != null
                   && avatarDescriptor.expressionParameters != null;
        }

        private void DrawPages(SimpleFacialExpressionLanguage language)
        {
            if (_pages.arraySize == 0)
            {
                _pages.InsertArrayElementAtIndex(0);
            }

            for (var pageIndex = 0; pageIndex < _pages.arraySize; pageIndex++)
            {
                var page = _pages.GetArrayElementAtIndex(pageIndex);
                var slots = page.FindPropertyRelative(nameof(SimpleFacialExpressionPage.slots));

                EnsureSerializedSlotCount(slots);

                GetSlotList(slots, pageIndex, language).DoLayoutList();

                EditorGUILayout.Space(4);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_pages.arraySize >= SimpleFacialExpressionMenu.MaxPages))
                {
                    if (GUILayout.Button(
                            SimpleFacialExpressionLocalization.Text(language, "addPage"),
                            GUILayout.Height(24)))
                    {
                        _pages.InsertArrayElementAtIndex(_pages.arraySize);
                        var page = _pages.GetArrayElementAtIndex(_pages.arraySize - 1);
                        var slots = page.FindPropertyRelative(nameof(SimpleFacialExpressionPage.slots));
                        slots.ClearArray();
                        EnsureSerializedSlotCount(slots);
                    }
                }

                using (new EditorGUI.DisabledScope(_pages.arraySize <= 1))
                {
                    if (GUILayout.Button(
                            SimpleFacialExpressionLocalization.Text(language, "removeLastPage"),
                            GUILayout.Height(24)))
                    {
                        _pages.DeleteArrayElementAtIndex(_pages.arraySize - 1);
                    }
                }
            }

            if (_pages.arraySize >= SimpleFacialExpressionMenu.MaxPages)
            {
                EditorGUILayout.HelpBox(
                    string.Format(SimpleFacialExpressionLocalization.Text(language, "maxPages"), SimpleFacialExpressionMenu.MaxPages),
                    MessageType.Info);
            }
        }

        private ReorderableList GetSlotList(SerializedProperty slots, int pageIndex, SimpleFacialExpressionLanguage language)
        {
            var key = slots.propertyPath;
            if (_slotLists.TryGetValue(key, out var list))
            {
                UpdateListCallbacks(list, slots, pageIndex, language);
                return list;
            }

            list = new ReorderableList(serializedObject, slots, true, true, true, true);
            _slotLists[key] = list;
            UpdateListCallbacks(list, slots, pageIndex, language);
            return list;
        }

        private static void EnsureSerializedSlotCount(SerializedProperty slots)
        {
            while (slots.arraySize > SimpleFacialExpressionMenu.SlotsPerPage)
            {
                slots.DeleteArrayElementAtIndex(slots.arraySize - 1);
            }

            while (slots.arraySize < SimpleFacialExpressionMenu.SlotsPerPage)
            {
                slots.InsertArrayElementAtIndex(slots.arraySize);
                ClearSlot(slots.GetArrayElementAtIndex(slots.arraySize - 1));
            }
        }

        private static void UpdateListCallbacks(
            ReorderableList list,
            SerializedProperty slots,
            int pageIndex,
            SimpleFacialExpressionLanguage language)
        {
            list.serializedProperty = slots;
            list.displayAdd = false;
            list.displayRemove = false;
            list.elementHeight = EditorGUIUtility.singleLineHeight + 6;
            list.headerHeight = EditorGUIUtility.singleLineHeight + 6;

            list.drawHeaderCallback = rect =>
            {
                rect.y += 2;
                EditorGUI.LabelField(
                    rect,
                    string.Format(SimpleFacialExpressionLocalization.Text(language, "page"), pageIndex + 1),
                    EditorStyles.boldLabel);
            };

            list.drawElementCallback = (rect, index, active, focused) =>
            {
                DrawSlot(rect, slots.GetArrayElementAtIndex(index));
            };

            list.onCanAddCallback = _ => false;
            list.onCanRemoveCallback = _ => false;
        }

        private static void ClearSlot(SerializedProperty slot)
        {
            slot.FindPropertyRelative(nameof(SimpleFacialExpressionSlot.displayName)).stringValue = string.Empty;
            slot.FindPropertyRelative(nameof(SimpleFacialExpressionSlot.animation)).objectReferenceValue = null;
        }

        private static void DrawSlot(Rect rect, SerializedProperty slot)
        {
            var displayName = slot.FindPropertyRelative(nameof(SimpleFacialExpressionSlot.displayName));
            var animation = slot.FindPropertyRelative(nameof(SimpleFacialExpressionSlot.animation));

            rect.y += 1;
            rect.height = EditorGUIUtility.singleLineHeight;
            var nameRect = new Rect(rect.x, rect.y, rect.width * 0.46f, rect.height);
            var animationRect = new Rect(nameRect.xMax + 6, rect.y, rect.xMax - nameRect.xMax - 6, rect.height);

            displayName.stringValue = EditorGUI.TextField(nameRect, displayName.stringValue);
            EditorGUI.PropertyField(animationRect, animation, GUIContent.none);
        }

    }
}
