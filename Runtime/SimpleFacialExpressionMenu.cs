using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace SimpleFacialExpressionMenuTool
{
    public enum SimpleFacialExpressionLanguage
    {
        Japanese,
        Korean,
        English
    }

    [Serializable]
    public class SimpleFacialExpressionSlot
    {
        public string displayName;
        public AnimationClip animation;
    }

    [Serializable]
    public class SimpleFacialExpressionPage
    {
        public List<SimpleFacialExpressionSlot> slots = new List<SimpleFacialExpressionSlot>();

        public void EnsureSlotCount()
        {
            if (slots == null)
            {
                slots = new List<SimpleFacialExpressionSlot>();
            }

            while (slots.Count > SimpleFacialExpressionMenu.SlotsPerPage)
            {
                slots.RemoveAt(slots.Count - 1);
            }

            while (slots.Count < SimpleFacialExpressionMenu.SlotsPerPage)
            {
                slots.Add(new SimpleFacialExpressionSlot());
            }

            for (var i = 0; i < slots.Count; i++)
            {
                if (slots[i] == null)
                {
                    slots[i] = new SimpleFacialExpressionSlot();
                }
            }
        }
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Simple Facial Expression Menu/Simple Facial Expression Menu")]
    public class SimpleFacialExpressionMenu : MonoBehaviour, IEditorOnly
    {
        public const int SlotsPerPage = 5;
        public const int FloatQuantizedValueCount = 255;
        public const int ReservedFloatValueCount = 2;
        public const float ResetMenuValue = 1f;
        public const float InactiveDummyMenuValue = -1f;
        public const int MaxPages = (FloatQuantizedValueCount - ReservedFloatValueCount) / SlotsPerPage;
        public const float DefaultThumbnailCameraOffsetX = 0f;
        public const float DefaultThumbnailCameraOffsetY = 0f;
        public const float DefaultThumbnailCameraZoom = 1f;
        public const float DefaultThumbnailPadding = 0f;
        public const float MinThumbnailCameraOffset = -0.25f;
        public const float MaxThumbnailCameraOffset = 0.25f;
        public const float MinThumbnailCameraZoom = 0.5f;
        public const float MaxThumbnailCameraZoom = 3f;
        public const float MinThumbnailPadding = 0f;
        public const float MaxThumbnailPadding = 0.5f;
        public const int DefaultLayerPriority = 230;

        public Texture2D rootMenuIcon;
        public string rootMenuName = string.Empty;
        public VRCExpressionsMenu installTargetMenu;
        public string parameterName = "SFM_Facial";
        public bool syncedParameter = true;
        public List<SimpleFacialExpressionPage> pages = new List<SimpleFacialExpressionPage>();
        public float thumbnailCameraOffsetX = DefaultThumbnailCameraOffsetX;
        public float thumbnailCameraOffsetY = DefaultThumbnailCameraOffsetY;
        public float thumbnailCameraZoom = DefaultThumbnailCameraZoom;
        public float thumbnailPadding = DefaultThumbnailPadding;
        public int layerPriority;
        public bool writeDefaults = true;
        public SimpleFacialExpressionLanguage language = SimpleFacialExpressionLanguage.Japanese;

        private void Reset()
        {
            EnsureValidState();
        }

        private void OnValidate()
        {
            EnsureValidState();
        }

        public void EnsureValidState()
        {
            if (pages == null)
            {
                pages = new List<SimpleFacialExpressionPage>();
            }

            if (pages.Count == 0)
            {
                pages.Add(new SimpleFacialExpressionPage());
            }

            while (pages.Count > MaxPages)
            {
                pages.RemoveAt(pages.Count - 1);
            }

            foreach (var page in pages)
            {
                if (page == null)
                {
                    continue;
                }

                page.EnsureSlotCount();
            }

            for (var i = 0; i < pages.Count; i++)
            {
                if (pages[i] == null)
                {
                    pages[i] = new SimpleFacialExpressionPage();
                    pages[i].EnsureSlotCount();
                }
            }

            if (string.IsNullOrWhiteSpace(parameterName))
            {
                parameterName = "SFM_Facial";
            }

            thumbnailCameraOffsetX = Mathf.Clamp(
                thumbnailCameraOffsetX,
                MinThumbnailCameraOffset,
                MaxThumbnailCameraOffset);
            thumbnailCameraOffsetY = Mathf.Clamp(
                thumbnailCameraOffsetY,
                MinThumbnailCameraOffset,
                MaxThumbnailCameraOffset);
            thumbnailCameraZoom = Mathf.Clamp(
                thumbnailCameraZoom,
                MinThumbnailCameraZoom,
                MaxThumbnailCameraZoom);
            thumbnailPadding = Mathf.Clamp(
                thumbnailPadding,
                MinThumbnailPadding,
                MaxThumbnailPadding);
        }

    }

    public sealed class SimpleFacialExpressionGeneratedMarker : MonoBehaviour, IEditorOnly
    {
    }
}
