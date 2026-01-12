using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class PlayerAuthoring : MonoBehaviour
{
    private class Baker : Baker<PlayerAuthoring>
    {
        public override void Bake(PlayerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            // Add components to the entity
            AddComponent<PLayerTag>(entity);
        }
    }
}

public struct PLayerTag : IComponentData { }
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
                 .WithAll<PLayerTag>())
        {
            moveDirection.ValueRW.Value = currentInput;
        }
    }
}