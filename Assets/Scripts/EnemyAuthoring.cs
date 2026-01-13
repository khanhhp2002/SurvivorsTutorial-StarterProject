using System.ComponentModel;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine;

[RequireComponent(typeof(CharacterAuthoring))]
public class EnemyAuthoring : MonoBehaviour
{
    public float attackDamage;
    public float attackCooldown;
    private class Baker : Baker<EnemyAuthoring>
    {
        public override void Bake(EnemyAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            // Add components to the entity
            AddComponent<EnemyTag>(entity);
            AddComponent(entity, new EnemyAttackData
            {
                hitPoints = authoring.attackDamage,
                attackCooldown = authoring.attackCooldown
            });
            AddComponent(entity, new EnemyCooldownTimer { Value = 0.0 });
            SetComponentEnabled<EnemyCooldownTimer>(entity, false);
        }
    }
}

public struct EnemyAttackData : IComponentData
{
    public float hitPoints;
    public float attackCooldown;
}

public struct EnemyCooldownTimer : IComponentData, IEnableableComponent
{
    public double Value;
}

public struct EnemyTag : IComponentData { }

public partial struct EnemyMoveToPlayerSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Implementation for enemy movement towards player would go here
        var playerEntity = SystemAPI.GetSingletonEntity<PlayerTag>();
        var playerTransform = SystemAPI.GetComponent<LocalTransform>(playerEntity);
        var moveToPlayerJob = new EnemyMoveToPlayerJob
        {
            PlayerPosition = playerTransform.Position.xy
        };

        state.Dependency = moveToPlayerJob.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(EnemyTag))]
public partial struct EnemyMoveToPlayerJob : IJobEntity
{
    public float2 PlayerPosition;
    public void Execute(ref CharacterMoveDirection moveDirection, in LocalTransform transform)
    {
        // Implementation for enemy movement towards player would go here
        var direction = PlayerPosition - transform.Position.xy;
        moveDirection.Value = math.normalize(direction);
    }
}

[UpdateInGroup(typeof(PhysicsSystemGroup))]
[UpdateAfter(typeof(PhysicsSimulationGroup))]
[UpdateBefore(typeof(AfterPhysicsSystemGroup))]
public partial struct EnemyAttackSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EnemyTag>();
        state.RequireForUpdate<PlayerTag>();
        state.RequireForUpdate<SimulationSingleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var elapsedTime = SystemAPI.Time.ElapsedTime;
        foreach (var (expirationTime, cooldownEnabled) in
                 SystemAPI.Query<EnemyCooldownTimer, EnabledRefRW<EnemyCooldownTimer>>())
        {
            if (elapsedTime < expirationTime.Value) continue;

            cooldownEnabled.ValueRW = false;
        }

        var enemyAttackJob = new EnemyAttackJob
        {
            PlayerTagLookup = SystemAPI.GetComponentLookup<PlayerTag>(true),
            EnemyAttackDataLookup = SystemAPI.GetComponentLookup<EnemyAttackData>(true),
            EnemyCooldownTimerLookup = SystemAPI.GetComponentLookup<EnemyCooldownTimer>(false),
            ElapsedTime = elapsedTime,
            DamageBufferLookup = SystemAPI.GetBufferLookup<DamageThisFrame>(false)
        };

        var simulation = SystemAPI.GetSingleton<SimulationSingleton>();
        state.Dependency = enemyAttackJob.Schedule(simulation, state.Dependency);
    }
}

[BurstCompile]
public struct EnemyAttackJob : ICollisionEventsJob
{
    [Unity.Collections.ReadOnly] public ComponentLookup<PlayerTag> PlayerTagLookup;
    [Unity.Collections.ReadOnly] public ComponentLookup<EnemyAttackData> EnemyAttackDataLookup;
    public ComponentLookup<EnemyCooldownTimer> EnemyCooldownTimerLookup;
    public double ElapsedTime;
    public BufferLookup<DamageThisFrame> DamageBufferLookup;

    public void Execute(CollisionEvent collisionEvent)
    {
        Entity playerEntity = default;
        Entity enemyEntity = default;

        if (PlayerTagLookup.HasComponent(collisionEvent.EntityA) && EnemyAttackDataLookup.HasComponent(collisionEvent.EntityB))
        {
            playerEntity = collisionEvent.EntityA;
            enemyEntity = collisionEvent.EntityB;
        }
        else if (PlayerTagLookup.HasComponent(collisionEvent.EntityB) && EnemyAttackDataLookup.HasComponent(collisionEvent.EntityA))
        {
            playerEntity = collisionEvent.EntityB;
            enemyEntity = collisionEvent.EntityA;
        }
        else
        {
            return; // No player involved in this collision
        }

        // Enemy still in cooldown
        if(EnemyCooldownTimerLookup.IsComponentEnabled(enemyEntity)) return;

        var attackData = EnemyAttackDataLookup[enemyEntity];

        // Reset cooldown
        EnemyCooldownTimerLookup[enemyEntity] = new EnemyCooldownTimer
        {
            Value = ElapsedTime + attackData.attackCooldown
        };

        // Enable the cooldown component to make timer running
        EnemyCooldownTimerLookup.SetComponentEnabled(enemyEntity, true);

        // Apply damage to player
        var damageBuffer = DamageBufferLookup[playerEntity];
        damageBuffer.Add(new DamageThisFrame { Value = attackData.hitPoints });
    }
}
