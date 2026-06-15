using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using nadena.dev.ndmf.fluent;
using SimpleFacialExpressionMenuTool;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using Object = UnityEngine.Object;

[assembly: ExportsPlugin(typeof(SimpleFacialExpressionMenuTool.Editor.SimpleFacialExpressionMenuPlugin))]

namespace SimpleFacialExpressionMenuTool.Editor
{
    [RunsOnPlatforms(WellKnownPlatforms.VRChatAvatar30)]
    internal sealed class SimpleFacialExpressionMenuPlugin : Plugin<SimpleFacialExpressionMenuPlugin>
    {
        private const string EllipsisIconPath = "Packages/me.kirisame.sfem/Editor/Icon/Ellipsis.png";
        private const string CrossIconPath = "Packages/me.kirisame.sfem/Editor/Icon/cross.png";
        private const int GeneratedIconSize = 256;
        private const int GeneratedIconLayer = 31;
        private const string BlendShapePrefix = "blendShape.";
        private const float ActivationEpsilon = 0.001f;
        private const string GestureLeftParameter = "GestureLeft";
        private const string GestureRightParameter = "GestureRight";

        public override string QualifiedName => "simple-facial-expression-menu";
        public override string DisplayName => "Simple Facial Expression Menu";

        protected override void Configure()
        {
            InPhase(BuildPhase.Generating)
                .Run("Generate Modular Avatar menu components", GenerateModularAvatarComponents);

            InPhase(BuildPhase.Transforming)
                .AfterPlugin("nadena.dev.modular-avatar")
                .WithRequiredExtension(typeof(AnimatorServicesContext), sequence =>
                {
                    sequence.Run("Generate facial expression animator", GenerateAnimator);
                });
        }

        private static void GenerateModularAvatarComponents(BuildContext context)
        {
            foreach (var component in context.AvatarRootObject.GetComponentsInChildren<SimpleFacialExpressionMenu>(true))
            {
                component.EnsureValidState();
                if (!HasValidAvatarDescriptor(component))
                {
                    continue;
                }

                var transparentIcon = CreateTransparentIcon(context);
                GenerateMenuHierarchy(context, component, transparentIcon);
            }
        }

        private static Texture2D CreateTransparentIcon(BuildContext context)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.name = "Simple Facial Expression Transparent Icon";
            texture.SetPixels(new[]
            {
                new Color(0, 0, 0, 0),
                new Color(0, 0, 0, 0),
                new Color(0, 0, 0, 0),
                new Color(0, 0, 0, 0)
            });
            texture.Apply();
            context.AssetSaver.SaveAsset(texture);
            return texture;
        }

        private static void GenerateMenuHierarchy(BuildContext context, SimpleFacialExpressionMenu component, Texture2D transparentIcon)
        {
            component.EnsureValidState();

            var root = new GameObject("__SFEM_Generated_" + component.GetInstanceID());
            root.transform.SetParent(component.transform, false);
            root.AddComponent<SimpleFacialExpressionGeneratedMarker>();

            var installer = root.AddComponent<ModularAvatarMenuInstaller>();
            installer.installTargetMenu = component.installTargetMenu;

            var group = root.AddComponent<ModularAvatarMenuGroup>();
            group.targetObject = root;

            var parameters = root.AddComponent<ModularAvatarParameters>();
            parameters.parameters.Add(new ParameterConfig
            {
                nameOrPrefix = component.parameterName,
                syncType = ParameterSyncType.Float,
                defaultValue = 0,
                saved = false,
                localOnly = !component.syncedParameter,
                internalParameter = false,
                isPrefix = false,
                hasExplicitDefaultValue = true
            });

            var rootMenu = CreateMenuItem(
                root.transform,
                "Root",
                RootMenuName(component),
                VRCExpressionsMenu.Control.ControlType.SubMenu,
                string.Empty,
                0,
                component.rootMenuIcon,
                true);

            var slotLabels = BuildSlotLabels(component);
            var slotIcons = BuildSlotIcons(context, component);
            var currentPageParent = rootMenu.transform;
            var resetIcon = LoadIcon(CrossIconPath);
            var nextIcon = LoadIcon(EllipsisIconPath);
            for (var pageIndex = 0; pageIndex < component.pages.Count; pageIndex++)
            {
                GeneratePage(component, currentPageParent, pageIndex, transparentIcon, resetIcon, nextIcon, slotLabels, slotIcons, out var nextPageParent);
                currentPageParent = nextPageParent;
            }
        }

        private static Texture2D LoadIcon(string path)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Dictionary<int, Texture2D> BuildSlotIcons(BuildContext context, SimpleFacialExpressionMenu component)
        {
            var icons = new Dictionary<int, Texture2D>();
            var avatarDescriptor = component.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (avatarDescriptor == null)
            {
                return icons;
            }

            var avatarRoot = avatarDescriptor.gameObject;
            var clipCache = new Dictionary<AnimationClip, Texture2D>();
            GameObject previewRoot = null;

            try
            {
                previewRoot = Object.Instantiate(avatarRoot);
                previewRoot.name = "__SFEM_IconPreview_" + component.GetInstanceID();
                previewRoot.hideFlags = HideFlags.HideAndDontSave;
                previewRoot.SetActive(true);
                SetLayerRecursively(previewRoot.transform, GeneratedIconLayer);

                for (var pageIndex = 0; pageIndex < component.pages.Count; pageIndex++)
                {
                    var page = component.pages[pageIndex];

                    for (var slotIndex = 0; slotIndex < SimpleFacialExpressionMenu.SlotsPerPage; slotIndex++)
                    {
                        var slot = GetSlotOrEmpty(page, slotIndex);
                        if (slot.animation == null)
                        {
                            continue;
                        }

                        if (!clipCache.TryGetValue(slot.animation, out var icon))
                        {
                            CopyBlendShapeWeights(avatarRoot, previewRoot);
                            ApplyBlendShapeLastKeys(previewRoot, slot.animation);
                            icon = CaptureFaceIcon(context, component, previewRoot, slot.animation.name);
                            if (icon != null)
                            {
                                clipCache[slot.animation] = icon;
                            }
                        }

                        if (icon != null)
                        {
                            icons[SlotKey(pageIndex, slotIndex)] = icon;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Simple Facial Expression Menu failed to generate facial icons: " + exception.Message);
            }
            finally
            {
                if (previewRoot != null)
                {
                    Object.DestroyImmediate(previewRoot);
                }
            }

            return icons;
        }

        private static void CopyBlendShapeWeights(GameObject sourceRoot, GameObject targetRoot)
        {
            foreach (var sourceRenderer in sourceRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var sourceMesh = sourceRenderer.sharedMesh;
                if (sourceMesh == null)
                {
                    continue;
                }

                var path = AnimationUtility.CalculateTransformPath(sourceRenderer.transform, sourceRoot.transform);
                var targetTransform = FindTransformByPath(targetRoot.transform, path);
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

        private static void ApplyBlendShapeLastKeys(GameObject targetRoot, AnimationClip clip)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!typeof(SkinnedMeshRenderer).IsAssignableFrom(binding.type)
                    || !binding.propertyName.StartsWith(BlendShapePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var targetTransform = FindTransformByPath(targetRoot.transform, binding.path);
                if (targetTransform == null || !targetTransform.TryGetComponent<SkinnedMeshRenderer>(out var renderer))
                {
                    continue;
                }

                var mesh = renderer.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                var shapeName = binding.propertyName.Substring(BlendShapePrefix.Length);
                var shapeIndex = mesh.GetBlendShapeIndex(shapeName);
                if (shapeIndex < 0)
                {
                    continue;
                }

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0)
                {
                    continue;
                }

                renderer.SetBlendShapeWeight(shapeIndex, curve.keys[curve.length - 1].value);
            }
        }

        private static Texture2D CaptureFaceIcon(
            BuildContext context,
            SimpleFacialExpressionMenu component,
            GameObject previewRoot,
            string clipName)
        {
            var renderTexture = RenderTexture.GetTemporary(
                GeneratedIconSize,
                GeneratedIconSize,
                24,
                RenderTextureFormat.ARGB32);
            var previousRenderTexture = RenderTexture.active;
            GameObject cameraObject = null;

            try
            {
                CalculateFaceCamera(
                    component,
                    previewRoot,
                    out var cameraPosition,
                    out var cameraRotation,
                    out var orthographicSize,
                    out var farClip);

                cameraObject = new GameObject("__SFEM_IconCamera");
                cameraObject.hideFlags = HideFlags.HideAndDontSave;
                var camera = cameraObject.AddComponent<Camera>();
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0, 0, 0, 0);
                camera.cullingMask = 1 << GeneratedIconLayer;
                camera.orthographic = true;
                camera.orthographicSize = orthographicSize;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = farClip;
                camera.allowHDR = false;
                camera.targetTexture = renderTexture;

                camera.Render();

                RenderTexture.active = renderTexture;
                var texture = new Texture2D(GeneratedIconSize, GeneratedIconSize, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, GeneratedIconSize, GeneratedIconSize), 0, 0);
                texture.Apply();
                ApplyThumbnailPaddingAndCircularMask(texture, component.thumbnailPadding);
                texture.name = "Simple Facial Expression Icon " + clipName;
                context.AssetSaver.SaveAsset(texture);
                return texture;
            }
            finally
            {
                RenderTexture.active = previousRenderTexture;
                RenderTexture.ReleaseTemporary(renderTexture);

                if (cameraObject != null)
                {
                    Object.DestroyImmediate(cameraObject);
                }
            }
        }

        private static void CalculateFaceCamera(
            SimpleFacialExpressionMenu component,
            GameObject previewRoot,
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
            var zoom = Mathf.Clamp(
                component.thumbnailCameraZoom,
                SimpleFacialExpressionMenu.MinThumbnailCameraZoom,
                SimpleFacialExpressionMenu.MaxThumbnailCameraZoom);
            var baseOrthographicSize = Mathf.Clamp(referenceHeight * 0.05f, 0.035f, 0.12f);

            target += right * (avatarHeight * component.thumbnailCameraOffsetX);
            target += up * (avatarHeight * component.thumbnailCameraOffsetY);
            cameraPosition = target + forward * distance;
            cameraRotation = Quaternion.LookRotation(target - cameraPosition, up);
            orthographicSize = Mathf.Max(baseOrthographicSize / zoom, 0.005f);
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

        private static Transform FindTransformByPath(Transform root, string path)
        {
            return string.IsNullOrEmpty(path) ? root : root.Find(path);
        }

        private static void SetLayerRecursively(Transform transform, int layer)
        {
            transform.gameObject.layer = layer;
            for (var childIndex = 0; childIndex < transform.childCount; childIndex++)
            {
                SetLayerRecursively(transform.GetChild(childIndex), layer);
            }
        }

        private static void GeneratePage(
            SimpleFacialExpressionMenu component,
            Transform parent,
            int pageIndex,
            Texture2D transparentIcon,
            Texture2D resetIcon,
            Texture2D nextIcon,
            Dictionary<int, string> slotLabels,
            Dictionary<int, Texture2D> slotIcons,
            out Transform nextPageParent)
        {
            var language = component.language;
            var resetText = SimpleFacialExpressionLocalization.Text(language, "reset");
            var nextText = SimpleFacialExpressionLocalization.Text(language, "next");
            var parameterName = component.parameterName;

            CreateMenuItem(
                parent,
                "Reset",
                resetText,
                VRCExpressionsMenu.Control.ControlType.Button,
                parameterName,
                SimpleFacialExpressionMenu.ResetMenuValue,
                resetIcon,
                false);

            var page = component.pages[pageIndex];
            for (var slotIndex = 0; slotIndex < SimpleFacialExpressionMenu.SlotsPerPage; slotIndex++)
            {
                var value = SlotValue(pageIndex, slotIndex);
                var slotNumber = SlotNumber(pageIndex, slotIndex);
                var label = MenuLabel(SlotLabel(slotLabels, pageIndex, slotIndex));
                slotIcons.TryGetValue(SlotKey(pageIndex, slotIndex), out var slotIcon);
                slotIcon = slotIcon != null ? slotIcon : transparentIcon;

                CreateMenuItem(
                    parent,
                    "Slot " + slotNumber,
                    label,
                    VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameterName,
                    value,
                    slotIcon,
                    false);
            }

            var isLastPage = pageIndex >= component.pages.Count - 1;
            if (isLastPage)
            {
                CreateMenuItem(
                    parent,
                    string.Empty,
                    string.Empty,
                    VRCExpressionsMenu.Control.ControlType.Button,
                    string.Empty,
                    SimpleFacialExpressionMenu.InactiveDummyMenuValue,
                    transparentIcon,
                    false);
                nextPageParent = parent;
                return;
            }

            var next = CreateMenuItem(
                parent,
                "Next",
                nextText,
                VRCExpressionsMenu.Control.ControlType.SubMenu,
                string.Empty,
                0,
                nextIcon,
                true);
            nextPageParent = next.transform;
        }

        private static ModularAvatarMenuItem CreateMenuItem(
            Transform parent,
            string objectName,
            string label,
            VRCExpressionsMenu.Control.ControlType type,
            string parameter,
            float value,
            Texture2D icon,
            bool childrenSubmenu)
        {
            var gameObject = new GameObject(objectName);
            gameObject.transform.SetParent(parent, false);

            var item = gameObject.AddComponent<ModularAvatarMenuItem>();
            item.label = label;
            item.Control = new VRCExpressionsMenu.Control
            {
                name = label,
                type = type,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter },
                value = value,
                icon = icon,
                subParameters = Array.Empty<VRCExpressionsMenu.Control.Parameter>(),
                labels = Array.Empty<VRCExpressionsMenu.Control.Label>()
            };
            item.MenuSource = childrenSubmenu ? SubmenuSource.Children : SubmenuSource.MenuAsset;
            item.isSynced = !string.IsNullOrEmpty(parameter);
            item.isSaved = false;
            item.isDefault = false;
            item.automaticValue = false;
            return item;
        }

        private static void GenerateAnimator(BuildContext context)
        {
            var components = context.AvatarRootObject
                .GetComponentsInChildren<SimpleFacialExpressionMenu>(true)
                .ToArray();

            foreach (var component in components)
            {
                component.EnsureValidState();
                if (!HasValidAvatarDescriptor(component))
                {
                    continue;
                }

                GenerateAnimatorForComponent(context, component);
            }

            CleanupGeneratedObjects(context);
        }

        private static bool HasValidAvatarDescriptor(SimpleFacialExpressionMenu component)
        {
            var avatarDescriptor = component.GetComponentInParent<VRCAvatarDescriptor>(true);
            return avatarDescriptor != null
                   && avatarDescriptor.customExpressions
                   && avatarDescriptor.expressionsMenu != null
                   && avatarDescriptor.expressionParameters != null;
        }

        private static void GenerateAnimatorForComponent(BuildContext context, SimpleFacialExpressionMenu component)
        {
            var animatorContext = context.Extension<AnimatorServicesContext>().ControllerContext;
            var fx = animatorContext.Controllers[VRCAvatarDescriptor.AnimLayerType.FX];
            var gestureFxLayers = !component.writeDefaults && component.disableGestureFxLayersWhenActive
                ? fx.Layers.Where(IsGestureDrivenFxLayer).ToArray()
                : Array.Empty<VirtualLayer>();
            fx.Parameters = fx.Parameters.SetItem(component.parameterName, new AnimatorControllerParameter
            {
                name = component.parameterName,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 0
            });

            var layer = fx.AddLayer(new LayerPriority(230), "Simple Facial Expression Menu: " + RootMenuName(component));
            layer.DefaultWeight = 1;
            layer.BlendingMode = AnimatorLayerBlendingMode.Override;

            var stateMachine = layer.StateMachine;
            var defaultState = stateMachine.AddState(
                "Default",
                animatorContext.Clone(CreateEmptyClip(context)),
                new Vector3(120, 120, 0));
            defaultState.WriteDefaultValues = component.writeDefaults;

            var expressionState = stateMachine.AddState(
                "Expressions",
                animatorContext.Clone(CreateExpressionBlendTree(context, component)),
                new Vector3(360, 120, 0));
            expressionState.WriteDefaultValues = component.writeDefaults;

            var expressionExitState = defaultState;
            if (gestureFxLayers.Length > 0)
            {
                expressionState.Behaviours = expressionState.Behaviours.AddRange(
                    CreateLayerWeightControls(gestureFxLayers, 0));

                var restoreGestureFxState = stateMachine.AddState(
                    "Restore Gesture FX Layers",
                    null,
                    new Vector3(360, 240, 0));
                restoreGestureFxState.WriteDefaultValues = false;
                restoreGestureFxState.Behaviours = restoreGestureFxState.Behaviours.AddRange(
                    CreateLayerWeightControls(gestureFxLayers, 1));
                restoreGestureFxState.Transitions = ImmutableList.Create(
                    CreateTransition(defaultState));
                expressionExitState = restoreGestureFxState;
            }

            defaultState.Transitions = ImmutableList.Create(
                CreateTransition(
                    expressionState,
                    CreateCondition(component.parameterName, AnimatorConditionMode.Greater, ActivationEpsilon)),
                CreateTransition(
                    expressionState,
                    CreateCondition(component.parameterName, AnimatorConditionMode.Less, -ActivationEpsilon)));
            expressionState.Transitions = ImmutableList.Create(
                CreateTransition(
                    expressionExitState,
                    CreateCondition(component.parameterName, AnimatorConditionMode.Greater, -ActivationEpsilon),
                    CreateCondition(component.parameterName, AnimatorConditionMode.Less, ActivationEpsilon)));
            stateMachine.DefaultState = defaultState;
        }

        private static IEnumerable<StateMachineBehaviour> CreateLayerWeightControls(
            IEnumerable<VirtualLayer> layers,
            float goalWeight)
        {
            foreach (var layer in layers)
            {
                var control = ScriptableObject.CreateInstance<VRCAnimatorLayerControl>();
                control.layer = layer.VirtualLayerIndex;
                control.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
                control.goalWeight = goalWeight;
                control.blendDuration = 0;
                yield return control;
            }
        }

        private static bool IsGestureDrivenFxLayer(VirtualLayer layer)
        {
            return layer.StateMachine != null
                   && StateMachineUsesGestureParameter(layer.StateMachine, new HashSet<VirtualStateMachine>());
        }

        private static bool StateMachineUsesGestureParameter(
            VirtualStateMachine stateMachine,
            HashSet<VirtualStateMachine> visited)
        {
            if (!visited.Add(stateMachine))
            {
                return false;
            }

            if (TransitionsUseGestureParameter(stateMachine.AnyStateTransitions)
                || TransitionsUseGestureParameter(stateMachine.EntryTransitions)
                || stateMachine.StateMachineTransitions.Values.Any(
                    transitions => TransitionsUseGestureParameter(transitions)))
            {
                return true;
            }

            foreach (var childState in stateMachine.States)
            {
                var state = childState.State;
                if (state != null
                    && (TransitionsUseGestureParameter(state.Transitions)
                        || IsGestureParameter(state.CycleOffsetParameter)
                        || IsGestureParameter(state.MirrorParameter)
                        || IsGestureParameter(state.SpeedParameter)
                        || IsGestureParameter(state.TimeParameter)
                        || MotionUsesGestureParameter(state.Motion)))
                {
                    return true;
                }
            }

            return stateMachine.StateMachines.Any(
                child => StateMachineUsesGestureParameter(child.StateMachine, visited));
        }

        private static bool TransitionsUseGestureParameter<TTransition>(IEnumerable<TTransition> transitions)
            where TTransition : VirtualTransitionBase
        {
            return transitions.Any(
                transition => transition.Conditions.Any(
                    condition => IsGestureParameter(condition.parameter)));
        }

        private static bool MotionUsesGestureParameter(VirtualMotion motion)
        {
            if (!(motion is VirtualBlendTree blendTree))
            {
                return false;
            }

            if (IsGestureParameter(blendTree.BlendParameter)
                || IsGestureParameter(blendTree.BlendParameterY))
            {
                return true;
            }

            return blendTree.Children.Any(
                child => IsGestureParameter(child.DirectBlendParameter)
                         || MotionUsesGestureParameter(child.Motion));
        }

        private static bool IsGestureParameter(string parameterName)
        {
            return string.Equals(parameterName, GestureLeftParameter, StringComparison.Ordinal)
                   || string.Equals(parameterName, GestureRightParameter, StringComparison.Ordinal);
        }

        private static AnimationClip CreateEmptyClip(BuildContext context)
        {
            var clip = new AnimationClip
            {
                name = "Simple Facial Expression Default",
                frameRate = 60
            };
            context.AssetSaver.SaveAsset(clip);
            return clip;
        }

        private static VirtualStateTransition CreateTransition(
            VirtualState destination,
            params AnimatorCondition[] conditions)
        {
            var transition = VirtualStateTransition.Create();
            transition.SetDestination(destination);
            transition.ExitTime = null;
            transition.Duration = 0;
            transition.HasFixedDuration = true;
            transition.CanTransitionToSelf = false;
            transition.Conditions = ImmutableList.Create(conditions);
            return transition;
        }

        private static AnimatorCondition CreateCondition(string parameterName, AnimatorConditionMode mode, float threshold)
        {
            return new AnimatorCondition
            {
                mode = mode,
                parameter = parameterName,
                threshold = threshold
            };
        }

        private static BlendTree CreateExpressionBlendTree(
            BuildContext context,
            SimpleFacialExpressionMenu component)
        {
            var avatarRoot = component.GetComponentInParent<VRCAvatarDescriptor>(true).gameObject;
            var blendShapeBindings = CollectBlendShapeBindings(component, avatarRoot, out var defaultValues);
            var neutralClip = CreateBlendShapeValueClip(context, "Simple Facial Expression Neutral", blendShapeBindings, null, defaultValues);
            var blendShapeClipCache = new Dictionary<AnimationClip, AnimationClip>();
            var children = new List<ChildMotion>
            {
                CreateChildMotion(neutralClip, 0),
                CreateChildMotion(neutralClip, SimpleFacialExpressionMenu.ResetMenuValue)
            };

            foreach (var clipSlot in CollectAnimationSlots(component))
            {
                var motionClip = clipSlot.Clip != null
                    ? GetOrCreateBlendShapeValueClip(context, clipSlot.Clip, blendShapeClipCache, blendShapeBindings, defaultValues)
                    : neutralClip;
                children.Add(CreateChildMotion(motionClip, clipSlot.Value));
            }

            return new BlendTree
            {
                name = "Expression Selector",
                blendType = BlendTreeType.Simple1D,
                blendParameter = component.parameterName,
                useAutomaticThresholds = false,
                children = children
                    .OrderBy(child => child.threshold)
                    .ToArray()
            };
        }

        private static ChildMotion CreateChildMotion(Motion motion, float threshold)
        {
            return new ChildMotion
            {
                motion = motion,
                threshold = threshold,
                timeScale = 1
            };
        }

        private static List<AnimationSlotInfo> CollectAnimationSlots(SimpleFacialExpressionMenu component)
        {
            var result = new List<AnimationSlotInfo>();

            for (var pageIndex = 0; pageIndex < component.pages.Count; pageIndex++)
            {
                var page = component.pages[pageIndex];

                for (var slotIndex = 0; slotIndex < SimpleFacialExpressionMenu.SlotsPerPage; slotIndex++)
                {
                    var slot = GetSlotOrEmpty(page, slotIndex);

                    result.Add(new AnimationSlotInfo
                    {
                        Clip = slot.animation,
                        Value = SlotValue(pageIndex, slotIndex)
                    });
                }
            }

            return result;
        }

        private static SimpleFacialExpressionSlot GetSlotOrEmpty(SimpleFacialExpressionPage page, int slotIndex)
        {
            if (page != null && page.slots != null && slotIndex < page.slots.Count && page.slots[slotIndex] != null)
            {
                return page.slots[slotIndex];
            }

            return new SimpleFacialExpressionSlot();
        }

        private static AnimationClip GetOrCreateBlendShapeValueClip(
            BuildContext context,
            AnimationClip sourceClip,
            Dictionary<AnimationClip, AnimationClip> cache,
            List<EditorCurveBinding> blendShapeBindings,
            Dictionary<string, float> defaultValues)
        {
            if (cache.TryGetValue(sourceClip, out var generatedClip))
            {
                return generatedClip;
            }

            generatedClip = CreateBlendShapeValueClip(
                context,
                "Simple Facial Expression " + sourceClip.name,
                blendShapeBindings,
                sourceClip,
                defaultValues);
            cache[sourceClip] = generatedClip;
            return generatedClip;
        }

        private static AnimationClip CreateBlendShapeValueClip(
            BuildContext context,
            string name,
            List<EditorCurveBinding> blendShapeBindings,
            AnimationClip sourceClip,
            Dictionary<string, float> defaultValues)
        {
            var clip = new AnimationClip
            {
                name = name,
                frameRate = sourceClip != null ? sourceClip.frameRate : 60
            };
            var sourceValues = sourceClip != null
                ? CollectBlendShapeLastKeyValues(sourceClip)
                : new Dictionary<string, float>();

            foreach (var binding in blendShapeBindings)
            {
                var key = BindingKey(binding);
                var value = sourceValues.TryGetValue(key, out var sourceValue)
                    ? sourceValue
                    : defaultValues.TryGetValue(key, out var defaultValue)
                        ? defaultValue
                        : 0;
                var curve = new AnimationCurve(new Keyframe(0, value))
                {
                    preWrapMode = WrapMode.ClampForever,
                    postWrapMode = WrapMode.ClampForever
                };
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }

            context.AssetSaver.SaveAsset(clip);
            return clip;
        }

        private static List<EditorCurveBinding> CollectBlendShapeBindings(
            SimpleFacialExpressionMenu component,
            GameObject avatarRoot,
            out Dictionary<string, float> defaultValues)
        {
            var bindings = new List<EditorCurveBinding>();
            var seen = new HashSet<string>();
            defaultValues = new Dictionary<string, float>();

            foreach (var clipSlot in CollectAnimationSlots(component))
            {
                if (clipSlot.Clip == null)
                {
                    continue;
                }

                foreach (var binding in AnimationUtility.GetCurveBindings(clipSlot.Clip))
                {
                    if (!IsBlendShapeBinding(binding))
                    {
                        continue;
                    }

                    var key = BindingKey(binding);
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    bindings.Add(binding);
                    defaultValues[key] = FindBlendShapeDefaultValue(avatarRoot, binding);
                }
            }

            bindings.Sort((left, right) => string.CompareOrdinal(BindingKey(left), BindingKey(right)));
            return bindings;
        }

        private static Dictionary<string, float> CollectBlendShapeLastKeyValues(AnimationClip sourceClip)
        {
            var values = new Dictionary<string, float>();
            foreach (var binding in AnimationUtility.GetCurveBindings(sourceClip))
            {
                if (!IsBlendShapeBinding(binding))
                {
                    continue;
                }

                var curve = AnimationUtility.GetEditorCurve(sourceClip, binding);
                if (curve == null || curve.length == 0)
                {
                    continue;
                }

                values[BindingKey(binding)] = curve.keys[curve.length - 1].value;
            }

            return values;
        }

        private static bool IsBlendShapeBinding(EditorCurveBinding binding)
        {
            return typeof(SkinnedMeshRenderer).IsAssignableFrom(binding.type)
                   && binding.propertyName.StartsWith(BlendShapePrefix, StringComparison.Ordinal);
        }

        private static float FindBlendShapeDefaultValue(GameObject avatarRoot, EditorCurveBinding binding)
        {
            var targetTransform = FindTransformByPath(avatarRoot.transform, binding.path);
            if (targetTransform == null || !targetTransform.TryGetComponent<SkinnedMeshRenderer>(out var renderer))
            {
                return 0;
            }

            var mesh = renderer.sharedMesh;
            if (mesh == null)
            {
                return 0;
            }

            var shapeName = binding.propertyName.Substring(BlendShapePrefix.Length);
            var shapeIndex = mesh.GetBlendShapeIndex(shapeName);
            return shapeIndex >= 0 ? renderer.GetBlendShapeWeight(shapeIndex) : 0;
        }

        private static string BindingKey(EditorCurveBinding binding)
        {
            return binding.path + "|" + binding.type.FullName + "|" + binding.propertyName;
        }

        private static void CleanupGeneratedObjects(BuildContext context)
        {
            foreach (var marker in context.AvatarRootObject.GetComponentsInChildren<SimpleFacialExpressionGeneratedMarker>(true))
            {
                Object.DestroyImmediate(marker.gameObject);
            }

            foreach (var component in context.AvatarRootObject.GetComponentsInChildren<SimpleFacialExpressionMenu>(true))
            {
                Object.DestroyImmediate(component);
            }
        }

        private static float SlotValue(int pageIndex, int slotIndex)
        {
            var code = -127 + SlotKey(pageIndex, slotIndex);
            if (code >= 0)
            {
                code++;
            }

            return code / 127f;
        }

        private static int SlotNumber(int pageIndex, int slotIndex)
        {
            return SlotKey(pageIndex, slotIndex) + 1;
        }

        private static Dictionary<int, string> BuildSlotLabels(SimpleFacialExpressionMenu component)
        {
            var labels = new Dictionary<int, string>();

            for (var pageIndex = 0; pageIndex < component.pages.Count; pageIndex++)
            {
                var page = component.pages[pageIndex];

                for (var slotIndex = 0; slotIndex < SimpleFacialExpressionMenu.SlotsPerPage; slotIndex++)
                {
                    var slot = GetSlotOrEmpty(page, slotIndex);
                    var key = SlotKey(pageIndex, slotIndex);
                    labels[key] = string.IsNullOrWhiteSpace(slot.displayName)
                        ? string.Empty
                        : slot.displayName;
                }
            }

            return labels;
        }

        private static int SlotKey(int pageIndex, int slotIndex)
        {
            return pageIndex * SimpleFacialExpressionMenu.SlotsPerPage + slotIndex;
        }

        private static string SlotLabel(Dictionary<int, string> slotLabels, int pageIndex, int slotIndex)
        {
            return slotLabels.TryGetValue(SlotKey(pageIndex, slotIndex), out var label)
                ? label
                : string.Empty;
        }

        private static string MenuLabel(string label)
        {
            return string.IsNullOrEmpty(label) ? " " : label;
        }

        private static string RootMenuName(SimpleFacialExpressionMenu component)
        {
            return string.IsNullOrWhiteSpace(component.rootMenuName)
                ? SimpleFacialExpressionLocalization.Text(component.language, "rootDefault")
                : component.rootMenuName;
        }

        private sealed class AnimationSlotInfo
        {
            public float Value;
            public AnimationClip Clip;
        }
    }
}
