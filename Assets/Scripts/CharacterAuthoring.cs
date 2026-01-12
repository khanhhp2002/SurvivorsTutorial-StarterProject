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
            AddComponent<CharacterMoveDirection>(entity);
            AddComponent(entity, new CharacterMoveSpeed { Value = authoring.MoveSpeed });
        }
    }
}

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
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (moveDirection, moveSpeed, velocity) in
                 SystemAPI.Query<CharacterMoveDirection, CharacterMoveSpeed, RefRW<PhysicsVelocity>>())
        {
            float2 moveStep2d = moveDirection.Value * moveSpeed.Value;
            velocity.ValueRW.Linear = new float3(moveStep2d, 0f);
        }
    }
}