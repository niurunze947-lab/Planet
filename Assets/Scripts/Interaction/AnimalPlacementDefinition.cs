using UnityEngine;

namespace Planet.Interaction
{
    [CreateAssetMenu(menuName = "Planet/Animal Placement Definition", fileName = "AnimalPlacementSettings")]
    public sealed class AnimalPlacementDefinition : ScriptableObject
    {
        [Header("动物模型（空缺时使用立方体）")]
        [Tooltip("完整 Prefab，包含网格、材质、骨骼和 Animator。不要只拖入骨骼文件。")]
        public GameObject modelPrefab;
        [Tooltip("按模型最大尺寸缩放到当前占位立方体的尺寸，避免导入后太小。")]
        public bool normalizeSize = true;
        [Min(0.001f)] public float scaleMultiplier = 1f;
        public Vector3 rotationOffset;
        [Tooltip("模型底部自动对齐地表后，额外抬高的米数。")]
        public float groundOffsetMetres;

        [Header("骨骼与动作（留空则保留 Prefab 原设置）")]
        [Tooltip("模型内部 Animator 的相对路径；留空自动寻找。多个 Animator 时建议指定。")]
        public string animatorPath;
        public Avatar avatarOverride;
        public RuntimeAnimatorController controllerOverride;
        [Min(0f)] public float animationSpeed = 1f;

        public void Configure(GameObject visual)
        {
            // Keep the complete hierarchy and all SkinnedMeshRenderer bone references intact.
            Animator animator = null;
            if (!string.IsNullOrEmpty(animatorPath))
            {
                Transform target = visual.transform.Find(animatorPath);
                if (target != null) animator = target.GetComponent<Animator>();
                if (animator == null)
                    Debug.LogWarning("动物投放：Animator 路径无效，改用模型已有 Animator。", this);
            }
            if (animator == null) animator = visual.GetComponentInChildren<Animator>(true);
            if (animator == null && (controllerOverride != null || avatarOverride != null))
                animator = visual.AddComponent<Animator>();
            if (animator != null)
            {
                if (avatarOverride != null)
                {
                    if (avatarOverride.isValid) animator.avatar = avatarOverride;
                    else Debug.LogWarning("动物投放：Avatar 无效，保留模型原有骨骼配置。", this);
                }
                if (controllerOverride != null) animator.runtimeAnimatorController = controllerOverride;
                animator.speed = animationSpeed;
            }
            // Animation can play in place, but must not move the surface anchor.
            foreach (Animator item in visual.GetComponentsInChildren<Animator>(true))
                item.applyRootMotion = false;
            foreach (Rigidbody item in visual.GetComponentsInChildren<Rigidbody>(true))
            {
                item.isKinematic = true;
                item.useGravity = false;
            }
        }
    }
}
