﻿using System.Collections;
using System.Collections.Generic;
using System;
#if LEVEL_EDITOR != true
using UnityEngine;
#else
using System.Numerics;

public class RaycastHit
{
    public float distance;
}

public class Ray
{
    public Vector3 origin;
    public Vector3 direction;
    public Ray(Vector3 orig, Vector3 dir)
    {
        origin = orig;
        direction = dir;
    }
}
#endif


namespace VRTD.Gameplay
{
    public class EffectInstance
    {
        public ProjectileEffect Effect;
        public EnemyInstance Enemy;
        public float StartTime;
        public float LastTime;
        public bool Completed;
        public float ImpactHandicap = 1.0F;
        GameObject go;

        public EffectInstance(EnemyInstance enemy, ProjectileEffect effect, float waveStartTime)
        {
            Enemy = enemy;
            Effect = effect;
            StartTime = waveStartTime;
            LastTime = waveStartTime;
            Completed = false;
        }

        public float AdvanceAndReportImpact(float waveTime)
        {
            Utilities.Assert(!Completed);

            float impactPercent = 0.0F;

            if (Effect.EffectDuration == 0.0F)
            {
                // Entire impact at once
                impactPercent = 1.0F;
            }
            else
            {
                float elapsedTimeThisTick = waveTime - LastTime;
                float totalTimeElapsed = waveTime - StartTime;

                // If we went over the total time this tick, correct for the overage
                if (totalTimeElapsed > Effect.EffectDuration)
                {
                    elapsedTimeThisTick -= (totalTimeElapsed - Effect.EffectDuration);
                }

                impactPercent = elapsedTimeThisTick / Effect.EffectDuration;
            }

            if ((waveTime - StartTime) > Effect.EffectDuration)
            {
                Completed = true;
            }
            LastTime = waveTime;

            return (impactPercent * Effect.EffectImpact * ImpactHandicap);
        }

        public void Destroy()
        {
            if (null != go)
            {
                GameObjectFactory.Destroy(go);
                go = null;
            }
        }

    }

    public class ProjectileInstance
    {
        const float PROJECTILE_HEIGHT = 0.5F;
        public Projectile ProjectileType;
        public Vector3 Position;
        public float LastUpdateTime;
        public float FireTime;
        public EnemyInstance Enemy;
        public Ray Direction;
        float DistanceToTravel;
        public bool IsComplete;
        GameObject go = null;
        bool heatSeeking = false;
        LevelDescription LevelDesc;

        //
        // Heat seeking
        //
        public ProjectileInstance(Projectile projectile, Vector3 sourcePos, EnemyInstance enemy, float waveTimeFired, LevelDescription desc)
        {
            LevelDesc = desc;
            ProjectileType = projectile;
            LastUpdateTime = waveTimeFired;
            FireTime = waveTimeFired;
#if LEVEL_EDITOR
            Position = new Vector3(sourcePos.X, PROJECTILE_HEIGHT, sourcePos.X);
#else
            Position = new Vector3(sourcePos.x, PROJECTILE_HEIGHT, sourcePos.z);
#endif
            Enemy = enemy;
            IsComplete = false;
            go = GameObjectFactory.InstantiateObject(ProjectileType.Asset);
            GameObjectFactory.SetMapPos(go, Position);
            heatSeeking = true;
        }


        //
        // Directional
        //
        public ProjectileInstance(Projectile projectile, Vector3 sourcePos, Vector3 direction, float distance, float waveTimeFired, LevelDescription desc)
        {
            LevelDesc = desc;
            ProjectileType = projectile;
            DistanceToTravel = distance;
            LastUpdateTime = waveTimeFired;
            FireTime = waveTimeFired;
#if LEVEL_EDITOR
            Position = new Vector3(sourcePos.X, PROJECTILE_HEIGHT, sourcePos.X);
            Vector3 normalized = Vector3.Normalize(direction);
            Direction = new Ray(sourcePos, normalized);
#else
            Position = sourcePos;
            Direction = new Ray(sourcePos, direction.normalized);
#endif
            IsComplete = false;
            go = GameObjectFactory.InstantiateObject(ProjectileType.Asset);
            GameObjectFactory.SetMapPos(go, Position);
#if LEVEL_EDITOR == false
            go.transform.forward = direction;
#endif
            heatSeeking = false;
        }

        public void Advance(float waveTime)
        {
            if (heatSeeking)
            {
                AdvanceHeatSeeking(waveTime);
            }
            else
            {
                AdvanceDirectional(waveTime);
            }

        }

        void AdvanceDirectional(float waveTime)
        {
            float distanceMovedThisFrame = (float)(waveTime - LastUpdateTime) * ProjectileType.AirSpeed;
            LastUpdateTime = waveTime;

#if LEVEL_EDITOR
            Vector3 normalizedDir = Vector3.Normalize(Direction.direction);
            Vector3 progress = normalizedDir * distanceMovedThisFrame;
#else
            Vector3 progress = Direction.direction.normalized * distanceMovedThisFrame;
#endif

            Position += progress;
            Direction.origin = Position;

            // If we hit the target, apply the effects (damage, slow, etc)
            if (distanceMovedThisFrame >= DistanceToTravel)
            {
                IsComplete = true;
                DistanceToTravel = 0.0F;
                ProcessImpact(Position, waveTime);
                Destroy();
                return;
            }

            DistanceToTravel -= distanceMovedThisFrame;
            GameObjectFactory.SetWorldPos(go, Position);
#if LEVEL_EDITOR == false
            go.transform.forward = Direction.direction;
#endif
        }

      

        void AdvanceHeatSeeking(float waveTime)
        { 
            if (!Enemy.IsActive)
            {
                Utilities.Log("  enemy no longer active (projectile dead)");
                IsComplete = true;
                Destroy();
                return;
            }

            float hypotenuse = Vector3.Distance(Position, Enemy.Position);
            float distanceMovedThisFrame = (float)(waveTime - LastUpdateTime) * ProjectileType.AirSpeed;
            LastUpdateTime = waveTime;

            // If we hit the target, apply the effects (damage, slow, etc)
            if (distanceMovedThisFrame >= hypotenuse)
            {
                IsComplete = true;
                ProcessImpact(Enemy.Position, waveTime, Enemy);
                Destroy();
                return;
            }

#if LEVEL_EDITOR
            Vector3 direction = Enemy.Position - Position;
            direction = VectorHelpers.Normalize(direction);
#else
            Vector3 direction = (Enemy.Position - Position).normalized;
#endif
            Vector3 progress = direction * distanceMovedThisFrame;
            Position += progress;
            GameObjectFactory.SetMapPos(go, Position);
#if LEVEL_EDITOR == false
            go.transform.forward = direction;
#endif
        }


        void ProcessImpact(Vector3 pos, float waveTime, EnemyInstance enemy = null)
        {
            if (!string.IsNullOrEmpty(ProjectileType.ImpactAsset))
            {
#if LEVEL_EDITOR == false
                GameObject impactVisual = GameObjectFactory.InstantiateObject(ProjectileType.ImpactAsset);
                impactVisual.transform.position = Position;
#endif
            }

            for (int j = 0; j < ProjectileType.Effects.Count; j++)
            {
                EffectInstance projectileEffect = null;
                if (ProjectileType.Effects[j].EffectRadius == 0.0F)
                {
                    if (null == enemy)
                    {
                        throw new Exception("Projectiles with zero range need to be heat seeking");
                    }
                    projectileEffect = new EffectInstance(Enemy, ProjectileType.Effects[j], waveTime);
                    enemy.ActiveEffects.Add(projectileEffect);
                }
                else
                {
                    MapPos mapcontactpos = GameObjectFactory.WorldVec3ToMapPos(pos);

                    for (int i = 0; i < LevelDesc.Road.Count; i++)
                    {
                        MapPos roadItem = LevelDesc.Road[i];
                        float distanceFromRoadSegment = Vector3.Distance(mapcontactpos.Pos, roadItem.Pos);
                        if (distanceFromRoadSegment <= ProjectileType.Effects[j].EffectRadius)
                        {
                            for (int k = 0; k < roadItem.EnemiesOccupying.Count; k++)
                            {
                                projectileEffect = new EffectInstance(Enemy, ProjectileType.Effects[j], waveTime);
                                EnemyInstance currentEnemy = roadItem.EnemiesOccupying[j];
                                if (currentEnemy == enemy)
                                {
                                    // Direct hit -- apply major impact
                                    projectileEffect.ImpactHandicap = 1.0F;
                                }
                                else if (distanceFromRoadSegment < (ProjectileType.Effects[j].EffectRadius/2))
                                {
                                    // Within half of radius
                                    projectileEffect.ImpactHandicap = 0.5F;
                                }
                                else
                                {
                                    // outer radius
                                    projectileEffect.ImpactHandicap = 0.25F;
                                }
                                currentEnemy.ActiveEffects.Add(projectileEffect);
                            }
                        }
                    }
                }
            }
        }


        public void Destroy()
        {
            if (null != go)
            {
                try
                {
                    GameObjectFactory.Destroy(go);
                }catch (Exception e)
                {
                    Utilities.Log(e.Message);
                }
                go = null;
            }
        }
    }

    public class ProjectileManager
    {
        public delegate Projectile ProjectileDefCallback(string projectileName);
        public ProjectileDefCallback ProjectileReader;

        public List<ProjectileInstance> ProjectilesInFlight = null;
        LevelDescription LevelDesc = null;

        public ProjectileManager(LevelDescription desc, ProjectileDefCallback readerCallback = null)
        {
            LevelDesc = desc;
            ProjectilesInFlight = new List<ProjectileInstance>();

            if (null == readerCallback)
            {
                ProjectileReader = LevelLoader.LookupProjectile;
            }
            else
            {
                ProjectileReader = readerCallback;
            }
        }


        public void Fire(string projectileName, Vector3 origin, EnemyInstance target, float fireTime)
        {
            ProjectileInstance projectile = new ProjectileInstance(ProjectileReader(projectileName), origin, target, fireTime, LevelDesc);
            Utilities.Assert(target.HealthRemaining > 0.0F);
            ProjectilesInFlight.Add(projectile);
        }

        public void Fire(string projectileName, Vector3 origin, Vector3 direction, float distance, float fireTime)
        {
            ProjectileInstance projectile = new ProjectileInstance(ProjectileReader(projectileName), origin, direction, distance, fireTime, LevelDesc);
            ProjectilesInFlight.Add(projectile);
        }

        public void AdvanceAll(float waveTime)
        {
            for (int i = ProjectilesInFlight.Count; i > 0; i--)
            {
                ProjectilesInFlight[i - 1].Advance(waveTime);

                if (ProjectilesInFlight[i - 1].IsComplete)
                {
                    ProjectilesInFlight.Remove(ProjectilesInFlight[i - 1]);
                }
            }
        }

        public void DestroyAll()
        {
            for (int i = ProjectilesInFlight.Count; i > 0; i--)
            {
                ProjectileInstance p = ProjectilesInFlight[i - 1];
                p.Destroy();
                ProjectilesInFlight.Remove(p);
            }
        }
    }
}