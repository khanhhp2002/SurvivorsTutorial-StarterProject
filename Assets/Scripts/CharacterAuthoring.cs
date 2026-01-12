using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

public class CharacterAuthoring : MonoBehaviour
{
    public float MoveSpeed;
    private class Baker : Baker<CharacterAuthoring>
    {
        public override void Bake(CharacterAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            // Add components to the entity
            AddComponent<InitializedCharacterFlag>(entity);
            AddComponent<CharacterMoveDirection>(entity);
            AddComponent(entity, new CharacterMoveSpeed { Value = authoring.MoveSpeed });
        }
    }
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

public partial struct CharacterMoveSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (moveDirection, moveSpeed, velocity) in
                 SystemAPI.Query<CharacterMoveDirection, CharacterMoveSpeed, RefRW<PhysicsVelocity>>())
        {
            float2 moveStep2d = moveDirection.Value * moveSpeed.Value;
            velocity.ValueRW.Linear = new float3(moveStep2d, 0f);
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