using Unity.Entities;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine;

public class PlasmaBlastAuthoring : MonoBehaviour
{
    public float Speed = 10f;
    public float Damage = 25f;
    public float duration = .5f;

    private class Baker : Baker<PlasmaBlastAuthoring>
    {
        public override void Bake(PlasmaBlastAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new PlasmaBlastData
            {
                Speed = authoring.Speed,
                Damage = authoring.Damage,
                duration = authoring.duration
            });
            AddComponent<DestroyEntityFlag>(entity);
            SetComponentEnabled<DestroyEntityFlag>(entity, false);
        }
    }
}

public struct PlasmaBlastData : IComponentData
{
    public float Speed;
    public float Damage;
    public float duration;
}

public partial struct PlasmaBlastMoveSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlasmaBlastData>();
    }

    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (plasmaBlastData, transform, entity) in
                 SystemAPI.Query<RefRW<PlasmaBlastData>, RefRW<LocalTransform>>().WithEntityAccess())
        {
            var moveDistance = plasmaBlastData.ValueRO.Speed * deltaTime;
            transform.ValueRW.Position += transform.ValueRW.Right() * moveDistance;

            plasmaBlastData.ValueRW.duration -= deltaTime;
            if (plasmaBlastData.ValueRW.duration <= 0f)
            {
                // Enable the DestroyEntityFlag to mark for destruction
                var destroyFlag = SystemAPI.GetComponentLookup<DestroyEntityFlag>();
                destroyFlag.SetComponentEnabled(entity, true);
            }
        }
    }
}

[UpdateInGroup(typeof(PhysicsSystemGroup))]
[UpdateAfter(typeof(PhysicsSimulationGroup))]
[UpdateBefore(typeof(AfterPhysicsSystemGroup))]
public partial struct PlasmaBlastAttackSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var plasmaBlastDataLookup = SystemAPI.GetComponentLookup<PlasmaBlastData>(true);
        var enemyTagLookup = SystemAPI.GetComponentLookup<EnemyTag>(true);
        var damageBufferLookup = SystemAPI.GetBufferLookup<DamageThisFrame>();
        var DestroyEntityFlagLookup = SystemAPI.GetComponentLookup<DestroyEntityFlag>();


        var job = new PlasmaBlastAttackJob
        {
            PlasmaBlastDataLookup = plasmaBlastDataLookup,
            EnemyTagLookup = enemyTagLookup,
            DamageBufferLookup = damageBufferLookup,
            DestroyEntityFlagLookup = DestroyEntityFlagLookup
        };

        var simulation = SystemAPI.GetSingleton<SimulationSingleton>();
        state.Dependency = job.Schedule(simulation, state.Dependency);
    }
}

public struct PlasmaBlastAttackJob : ITriggerEventsJob
{
    [Unity.Collections.ReadOnly] public ComponentLookup<PlasmaBlastData> PlasmaBlastDataLookup;
    [Unity.Collections.ReadOnly] public ComponentLookup<EnemyTag> EnemyTagLookup;
    public ComponentLookup<DestroyEntityFlag> DestroyEntityFlagLookup;
    public BufferLookup<DamageThisFrame> DamageBufferLookup;

    public void Execute(TriggerEvent triggerEvent)
    {
        Entity plasmaBlastEntity = default;
        Entity targetEntity = default;

        if (PlasmaBlastDataLookup.HasComponent(triggerEvent.EntityA) && EnemyTagLookup.HasComponent(triggerEvent.EntityB))
        {
            plasmaBlastEntity = triggerEvent.EntityA;
            targetEntity = triggerEvent.EntityB;
        }
        else if (PlasmaBlastDataLookup.HasComponent(triggerEvent.EntityB) && EnemyTagLookup.HasComponent(triggerEvent.EntityA))
        {
            plasmaBlastEntity = triggerEvent.EntityB;
            targetEntity = triggerEvent.EntityA;
        }
        else
        {
            return;
        }

        var attackDamage = PlasmaBlastDataLookup[plasmaBlastEntity].Damage;
        var enemyDamageBuffer = DamageBufferLookup[targetEntity];
        enemyDamageBuffer.Add(new DamageThisFrame { Value = attackDamage });

        DestroyEntityFlagLookup.SetComponentEnabled(plasmaBlastEntity, true);
    }
}
