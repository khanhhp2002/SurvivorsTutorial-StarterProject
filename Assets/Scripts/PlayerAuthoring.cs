using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

[RequireComponent(typeof(CharacterAuthoring))]
public class PlayerAuthoring : MonoBehaviour
{
    [SerializeField] private GameObject attackPrefab;
    [SerializeField] private float attackCooldown = 0.5f;
    [SerializeField] private float detectionSize;
    private class Baker : Baker<PlayerAuthoring>
    {
        public override void Bake(PlayerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            // Add components to the entity
            AddComponent<PlayerTag>(entity);
            AddComponent<InitialCameraTargetTag>(entity);
            AddComponent<CameraTarget>(entity);
            AddComponent(entity, new AnimationIndexOverride { Value = (float)PlayerAnimationIndex.Idle });

            var enemyLayer = LayerMask.NameToLayer("Enemy");
            var enemyLayerMask = (uint)math.pow(2, enemyLayer);
            var attackCollisionFilter = new CollisionFilter
            {
                BelongsTo = uint.MaxValue,
                CollidesWith = enemyLayerMask,
            };
            AddComponent(entity, new PlayerAttackData
            {
                AttackPrefab = GetEntity(authoring.attackPrefab, TransformUsageFlags.Dynamic),
                AttackCooldown = authoring.attackCooldown,
                DetectionSize = new float3(authoring.detectionSize),
                Filter = attackCollisionFilter
            });
            AddComponent(entity, new PlayerAttackCooldown { value = 0 });
        }
    }
}

[MaterialProperty("_AnimationIndex")]
public struct AnimationIndexOverride : IComponentData
{
    public float Value;
}

public struct PlayerAttackData : IComponentData
{
    public Entity AttackPrefab;
    public float AttackCooldown;
    public float3 DetectionSize;
    public CollisionFilter Filter;
}

public struct PlayerAttackCooldown : IComponentData
{
    public double value;
}

public struct PlayerTag : IComponentData { }
public struct InitialCameraTargetTag : IComponentData { }
public struct CameraTarget : IComponentData
{
    public UnityObjectRef<Transform> CameraTransform;
}

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct CameraInitializationSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<InitialCameraTargetTag>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!CameraTargetSingleton.Instance)
            return;

        var cameraTransform = CameraTargetSingleton.Instance.transform;

        var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

        foreach (var (cameraTarget, entity) in
                 SystemAPI.Query<RefRW<CameraTarget>>()
                 .WithAll<InitialCameraTargetTag, PlayerTag>()
                 .WithEntityAccess())
        {
            cameraTarget.ValueRW.CameraTransform = cameraTransform;
            ecb.RemoveComponent<InitialCameraTargetTag>(entity);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}

[UpdateAfter(typeof(TransformSystemGroup))]
public partial struct MoveCameraSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (transform, cameraTarget) in
                 SystemAPI.Query<LocalToWorld, CameraTarget>()
                 .WithAll<PlayerTag>()
                 .WithNone<InitialCameraTargetTag>())
        {
            cameraTarget.CameraTransform.Value.position = transform.Position;
        }
    }
}
public partial struct GlobalTimeUpdateSystem : ISystem
{
    private static int _globalTimeShaderPropertyID;
    public void OnCreate(ref SystemState state)
    {
        _globalTimeShaderPropertyID = Shader.PropertyToID("_GlobalTime");
    }

    public void OnUpdate(ref SystemState state)
    {
        float globalTime = (float)SystemAPI.Time.ElapsedTime;
        Shader.SetGlobalFloat(_globalTimeShaderPropertyID, globalTime);
    }
}
public partial class PlayerInputSystem : SystemBase
{
    private SurvivorsInput _input;

    protected override void OnCreate()
    {
        _input = new SurvivorsInput();
        _input.Enable();
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        var currentInput = (float2)_input.Player.Move.ReadValue<Vector2>();
        foreach (var moveDirection in
                 SystemAPI.Query<RefRW<CharacterMoveDirection>>()
                 .WithAll<PlayerTag>())
        {
            moveDirection.ValueRW.Value = currentInput;
        }
    }
}

public partial struct PlayerAttackSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<EnemyTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var elapsedTime = SystemAPI.Time.ElapsedTime;

        var ecb = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);

        var physicWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();

        foreach (var (attackData, cooldown, transform) in
                 SystemAPI.Query<PlayerAttackData, RefRW<PlayerAttackCooldown>, LocalTransform>()
                 .WithAll<PlayerTag>())
        {
            if (elapsedTime < cooldown.ValueRW.value)
                continue;

            // Instantiate attack entity
            var attackEntity = ecb.Instantiate(attackData.AttackPrefab);

            // Perform overlap check to find closest enemy
            var minDetectionPos = transform.Position - attackData.DetectionSize;
            var maxDetectionPos = transform.Position + attackData.DetectionSize;

            var aabb = new OverlapAabbInput
            {
                Aabb = new Unity.Physics.Aabb
                {
                    Min = minDetectionPos,
                    Max = maxDetectionPos
                },
                Filter = attackData.Filter
            };

            var overlapResults = new NativeList<int>(state.WorldUpdateAllocator);

            if (!physicWorldSingleton.OverlapAabb(aabb, ref overlapResults))
            {
                continue;
            }

            var minDistance = float.MaxValue;
            var closestEnemyPos = float3.zero;
            foreach (var colliderKey in overlapResults)
            {
                var bodyPos = physicWorldSingleton.Bodies[colliderKey].WorldFromBody.pos;
                var distance = math.distancesq(transform.Position, bodyPos);

                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestEnemyPos = bodyPos;
                }
            }

            //Direct the attack entity towards the closest enemy
            var direction = math.normalize(closestEnemyPos - transform.Position);
            var angle = math.atan2(direction.y, direction.x);
            var rotation = quaternion.Euler(0, 0, angle);

            // Set the position and rotation of the attack entity
            ecb.SetComponent(attackEntity, LocalTransform.FromPositionRotation(transform.Position, rotation));

            // Update cooldown
            cooldown.ValueRW.value = elapsedTime + attackData.AttackCooldown;
        }
    }
}
public enum PlayerAnimationIndex : byte
{
    Movement = 0,
    Idle = 1,
    None = byte.MaxValue
}