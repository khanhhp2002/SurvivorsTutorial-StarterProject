using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

public class PlayerAuthoring : MonoBehaviour
{
    private class Baker : Baker<PlayerAuthoring>
    {
        public override void Bake(PlayerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            // Add components to the entity
            AddComponent<PlayerTag>(entity);
            AddComponent<InitialCameraTargetTag>(entity);
            AddComponent<CameraTarget>(entity);
            AddComponent<AnimationIndexOverride>(entity, new AnimationIndexOverride { Value = (float)PlayerAnimationIndex.Idle });
        }
    }
}

[MaterialProperty("_AnimationIndex")]
public struct AnimationIndexOverride : IComponentData
{
    public float Value;
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

public enum PlayerAnimationIndex : byte
{
   Movement = 0,
   Idle = 1,
   None = byte.MaxValue
}