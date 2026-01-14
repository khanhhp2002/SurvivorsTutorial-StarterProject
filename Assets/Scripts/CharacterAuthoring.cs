using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using UnityEngine;

public class CharacterAuthoring : MonoBehaviour
{
    public float MoveSpeed;
    public float MaxHitPoints;
    private class Baker : Baker<CharacterAuthoring>
    {
        public override void Bake(CharacterAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            // Add components to the entity
            AddComponent<InitializedCharacterFlag>(entity);
            AddComponent<CharacterMoveDirection>(entity);
            AddComponent(entity, new CharacterMoveSpeed { Value = authoring.MoveSpeed });
            AddComponent(entity, new FacingDirectionOverride { Value = 1f });
            AddComponent(entity, new CharacterMaxHitPoints { Value = authoring.MaxHitPoints });
            AddComponent(entity, new CharacterCurrentHitPoints { Value = authoring.MaxHitPoints });
            AddBuffer<DamageThisFrame>(entity);
            AddComponent<DestroyEntityFlag>(entity);
            SetComponentEnabled<DestroyEntityFlag>(entity, false);
        }
    }
}

public struct CharacterMaxHitPoints : IComponentData
{
    public float Value;
}

public struct CharacterCurrentHitPoints : IComponentData
{
    public float Value;
}

public struct DamageThisFrame : IBufferElementData
{
    public float Value;
}

public struct InitializedCharacterFlag : IComponentData, IEnableableComponent { }

public struct CharacterMoveDirection : IComponentData
{
    public float2 Value;
}

public struct CharacterMoveSpeed : IComponentData
{
    public float Value;
}

[MaterialProperty("_FacingDirection")]
public struct FacingDirectionOverride : IComponentData
{
    public float Value;
}

public partial struct CharacterMoveSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (moveDirection, moveSpeed, velocity, facingDirection, entity) in
                 SystemAPI.Query<CharacterMoveDirection, CharacterMoveSpeed, RefRW<PhysicsVelocity>, RefRW<FacingDirectionOverride>>()
                 .WithEntityAccess())
        {
            float2 moveStep2d = moveDirection.Value * moveSpeed.Value;
            velocity.ValueRW.Linear = new float3(moveStep2d, 0f);

            if (math.abs(moveStep2d.x) > 0.15f)
            {
                facingDirection.ValueRW.Value = math.sign(moveStep2d.x);
            }

            if (SystemAPI.HasComponent<PlayerTag>(entity))
            {
                var animationOverride = SystemAPI.GetComponentRW<AnimationIndexOverride>(entity);
                animationOverride.ValueRW.Value = math.lengthsq(moveStep2d) > float.Epsilon ? (float)PlayerAnimationIndex.Movement : (float)PlayerAnimationIndex.Idle;
            }
        }
    }
}

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct CharacterInitializationSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (mass, shouldInitialize) in
                 SystemAPI.Query<RefRW<PhysicsMass>, EnabledRefRW<InitializedCharacterFlag>>())
        {
            // No rotation for characters
            mass.ValueRW.InverseInertia = float3.zero;
            shouldInitialize.ValueRW = false;
        }
    }
}

public partial struct ProcessDamageThisFrameSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (currentHitPoints, damageBuffer, entity) in
                 SystemAPI.Query<RefRW<CharacterCurrentHitPoints>, DynamicBuffer<DamageThisFrame>>()
                 .WithPresent<DestroyEntityFlag>() // Has to have DestroyEntityFlag component but ignore whether it's enabled or not
                 .WithEntityAccess())
        {
            if (damageBuffer.IsEmpty)
                continue;

            float totalDamage = 0f;
            for (int i = 0; i < damageBuffer.Length; i++)
            {
                totalDamage += damageBuffer[i].Value;
            }

            currentHitPoints.ValueRW.Value -= totalDamage;
            damageBuffer.Clear();

            if (currentHitPoints.ValueRO.Value <= 0)
            {
                SystemAPI.SetComponentEnabled<DestroyEntityFlag>(entity, true);
            }
        }
    }
}