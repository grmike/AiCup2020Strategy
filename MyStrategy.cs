using Aicup2020.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Action = Aicup2020.Model.Action;

namespace Aicup2020
{
    public class MyStrategy
    {
        public bool isFirstStep = true;
        Action result = new Action(new System.Collections.Generic.Dictionary<int, Model.EntityAction>(0));

        Random rnd = new Random();
        PlayerView CurrentView;

        Field[,] GlobalMap;
        bool HasResources = true;

        List<Army> MyArmies;
        Dictionary<int, Unit> MyUnits = new Dictionary<int, Unit>();
        Dictionary<int, Builder> MyBuilders = new Dictionary<int, Builder>();

        List<Entity> MyWorkers;
        Player MyPlayer;

        bool HasFirstBuilders = false;
        bool HasSecondMelees = false;
        bool HasMaxBuilders = false;
        EntityType? MustBuildType = EntityType.BuilderUnit;
        bool MustBuildHouse = false;
        bool IsNoEnemies = false;

        bool HasMeleeBase = false;
        bool MustBuildMeleeBase = false;
        bool HasRangedBase = false;
        bool Has2RangedBase = false;
        bool MustBuildRangedBase = false;


        Enemy RightEnemy; 
        Enemy TopEnemy;
        Enemy DiagonalEnemy;

        int ResourceDistance = 13;
        Vec2Int CenterBase = new Vec2Int(15, 15);
        Vec2Int CenterWorkersBase = new Vec2Int(7, 7);
        List<Vec2Int> Dangers = new List<Vec2Int>();
        List<Vec2Int> NearestResourses;
        List<Building> MyWoundedBuildings;
        List<int> HealingBuildings;

        double AllTroops;

        private int FIRST_BUILDERS_COUNT = 40;
        private RiseStrategy[] Strategy;
        private int SECOND_MELEES_COUNT = 0;
        private int MAX_BUILDERS_COUNT = 20;
        private int MAX_SOLDIERS_COUNT = 45;
        private int FULL_ARMY_COUNT = 10;
        private int GUARDIANS_ARMY_COUNT = 2;
        private int GUARDIANS_FORWARD_DISTANCE = 5;

        private double SOLDIERSxBUILDERS = 1;  // Archers / Builders
        private double ARCHERSxMELEES = 1.2;  // Melees / Archers
        private int HEAL_FOUND_DISTANCE = 5; // расстояние обнаружения кого вылечить
        private int HEAL_FOUND_DISTANCE_FOR_RANGE = 6; // расстояние обнаружения кого вылечить
        private int DANGER_DISTANCE = 10;

        private int HIGH_NUMBER_RESOURCES_FOR_UNITS = 100;
        private int HIGH_NUMBER_RESOURCES_FOR_BASES = 600;

        // Атака
        // Постройка
        // Ремонт
        // Движение



        public Action GetAction(PlayerView playerView, DebugInterface debugInterface)
        {
            CurrentView = playerView;

            if (isFirstStep)
            {
                isFirstStep = false;
                MyArmies = new List<Army>
                {
                    new Army{ Target = new Vec2Int(15, 20), State = ArmyState.Guard, Location = ArmyPosition.Top},
                    new Army{ Target = new Vec2Int(20, 15), State = ArmyState.Guard, Location = ArmyPosition.Right},
                    new Army{ Target = new Vec2Int(20, 20), State = ArmyState.Guard, Location = ArmyPosition.Diagonal}
                };

                var bases = CurrentView.Entities.Where(it => it.EntityType == EntityType.BuilderBase && it.PlayerId != CurrentView.MyId).ToList();

                TopEnemy = new Enemy { Position = new Vec2Int(15, CurrentView.MapSize - 15), Id = bases.FirstOrDefault(it => it.Position.X < 30).PlayerId ?? 0 };
                RightEnemy = new Enemy { Position = new Vec2Int(CurrentView.MapSize - 15, 15), Id = bases.FirstOrDefault(it => it.Position.Y < 30).PlayerId ?? 0 };
                DiagonalEnemy = new Enemy { 
                    Position = new Vec2Int(CurrentView.MapSize - 15, CurrentView.MapSize - 15), 
                    Id = bases.FirstOrDefault(it => it.Position.X > 30 && it.Position.Y > 30).PlayerId ?? 0 
                };

                RightEnemy.IsTarget = true;
                HealingBuildings = new List<int>();

                if (playerView.FogOfWar)
                {
                    FIRST_BUILDERS_COUNT = 35;
                    Strategy = new RiseStrategy[] { new RiseStrategy(40, 0.5), new RiseStrategy(50, 0.8), new RiseStrategy(1000, 1) };
                }
                else
                {
                    GUARDIANS_ARMY_COUNT = 0;
                    GUARDIANS_FORWARD_DISTANCE = 0;
                    FIRST_BUILDERS_COUNT = 40;
                    Strategy = new RiseStrategy[] { new RiseStrategy(1000, 1) };
                }
                
            }

            MyPlayer = CurrentView.Players.FirstOrDefault(it => it.Id == CurrentView.MyId);
            var myEntities = CurrentView.Entities.Where(it => it.PlayerId == CurrentView.MyId).ToList();
            MyWorkers = myEntities.Where(it => it.EntityType == EntityType.BuilderUnit).ToList();

            HasMeleeBase = myEntities.Any(it => it.EntityType == EntityType.MeleeBase && it.Active);
            HasRangedBase = myEntities.Any(it => it.EntityType == EntityType.RangedBase && it.Active);
            Has2RangedBase = myEntities.Count(it => it.EntityType == EntityType.RangedBase && it.Active) > 1;

            var buildersCount = Convert.ToDouble(MyWorkers.Count());
            var archersCount = Convert.ToDouble(myEntities.Count(it => it.EntityType == EntityType.RangedUnit));
            var meleesCount = Convert.ToDouble(myEntities.Count(it => it.EntityType == EntityType.MeleeUnit));

            AllTroops = buildersCount + archersCount + meleesCount;

            var currentPopulation = buildersCount * playerView.EntityProperties[EntityType.BuilderUnit].PopulationUse
                + archersCount * playerView.EntityProperties[EntityType.RangedUnit].PopulationUse
                + meleesCount * playerView.EntityProperties[EntityType.MeleeUnit].PopulationUse;

            var currentProvide = myEntities.Where(it => IsPopulateProvider(it) && it.Active).Sum(it => CurrentView.EntityProperties[it.EntityType].PopulationProvide);

            var MyHelpless = myEntities.Where(it => it.EntityType != EntityType.RangedUnit && it.EntityType != EntityType.MeleeUnit).ToList();
            var maxDangerX = MyHelpless.Any() ? MyHelpless.Max(it => it.Position.X) : 25;
            var maxDangerY = MyHelpless.Any() ? MyHelpless.Max(it => it.Position.Y) : 25;
            maxDangerX = Math.Max(maxDangerX, 25) + DANGER_DISTANCE;
            maxDangerY = Math.Max(maxDangerY, 25) + DANGER_DISTANCE;

            Dangers = new List<Vec2Int>();
            MyWoundedBuildings = new List<Building>();
            ResourceDistance = 2 * CurrentView.MapSize;

            GlobalMap = new Field[CurrentView.MapSize, CurrentView.MapSize];
            foreach (var entity in CurrentView.Entities)
            {
                var props = playerView.EntityProperties[entity.EntityType];
                if (props.Size == 1)
                {
                    GlobalMap[entity.Position.X, entity.Position.Y] = new Field
                    {
                        HasResource = entity.EntityType == EntityType.Resource,
                        HasBuilding = IsBuilding(entity),
                        HasSoldier = IsSoldier(entity),
                        HasBuilder = IsWorker(entity),
                        IsMine = entity.PlayerId == CurrentView.MyId
                    };

                    if (IsSoldier(entity) && entity.PlayerId != CurrentView.MyId && (entity.Position.X <= maxDangerX) && (entity.Position.Y <= maxDangerY))
                    {
                        var cellPos = new Vec2Int(entity.Position.X - entity.Position.X%3 + 1, entity.Position.Y - entity.Position.Y%3 + 1);
                        if (!Dangers.Any(it => IsEqual(it, cellPos))) Dangers.Add(cellPos);
                    }
                }
                else
                {
                    for (var i = -1; i <= props.Size; ++i)
                    {
                        for (var j = -1; j <= props.Size; ++j)
                        {
                            if ((entity.Position.X + i) < 0 || (entity.Position.Y + j) < 0) continue;
                            if ((entity.Position.X + i) >= CurrentView.MapSize || (entity.Position.Y + j) >= CurrentView.MapSize) continue;
                            GlobalMap[entity.Position.X + i, entity.Position.Y + j] = new Field
                            {
                                HasResource = entity.EntityType == EntityType.Resource,
                                HasBuilding = IsBuilding(entity),
                                HasSoldier = IsSoldier(entity),
                                HasBuilder = IsWorker(entity),
                                IsMine = entity.PlayerId == CurrentView.MyId
                            };
                        }
                    }
                }

                if (entity.EntityType == EntityType.Resource && entity.Health == props.MaxHealth)
                {
                    HasResources = true;
                    ResourceDistance = Math.Min(ResourceDistance, Distance(CenterWorkersBase, entity.Position));
                }

                if (entity.PlayerId == CurrentView.MyId && IsBuilding(entity) && entity.Health < props.MaxHealth)
                {
                    var delta = props.Size > 4 ? 2 : (props.Size > 2 ? 1 : 0);
                    MyWoundedBuildings.Add(new Building
                    {
                        Id = entity.Id,
                        Delta = 2*delta,
                        CenterPosition = new Vec2Int(entity.Position.X + delta, entity.Position.Y + delta),
                        IsUnitBase = entity.EntityType == EntityType.RangedBase || entity.EntityType == EntityType.MeleeBase
                    });
                }
            }

            NearestResourses = CurrentView.Entities.Where(it => it.EntityType == EntityType.Resource && it.Health == playerView.EntityProperties[it.EntityType].MaxHealth
                && Distance(CenterWorkersBase, it.Position) <= ResourceDistance).Select(it => it.Position).ToList();
            HealingBuildings = HealingBuildings.Where(it => !MyWoundedBuildings.Any(b => b.Id == it)).ToList();

            MustBuildHouse = currentPopulation == currentProvide && MyWoundedBuildings.Count() < 4;
            HasFirstBuilders = HasFirstBuilders || buildersCount >= FIRST_BUILDERS_COUNT;
            HasSecondMelees = HasSecondMelees || meleesCount >= SECOND_MELEES_COUNT;
            // MustBuildMeleeBase = false; // HasFirstBuilders && !HasMeleeBase;
            MustBuildMeleeBase = HasFirstBuilders && !myEntities.Any(it => it.EntityType == EntityType.MeleeBase);
            MustBuildRangedBase = HasFirstBuilders && !myEntities.Any(it => it.EntityType == EntityType.RangedBase);


            if (Dangers.Any())
            {
                MustBuildType = EntityType.MeleeUnit;
                if (!HasMeleeBase) MustBuildType = EntityType.RangedUnit;
                else 
                {
                    if (MustBuildType == EntityType.MeleeUnit && HasRangedBase)
                    {
                        if (meleesCount == 0) MustBuildType = EntityType.MeleeUnit;
                        else MustBuildType = (archersCount / meleesCount) < ARCHERSxMELEES ? EntityType.RangedUnit : EntityType.MeleeUnit;
                    }
                }
            }
            else if (!HasFirstBuilders)
            {
                MustBuildType = EntityType.BuilderUnit;
            }
            else if (!HasSecondMelees)
            {
                MustBuildType = EntityType.MeleeUnit;
            }
            else if (HasMeleeBase && HasRangedBase || (archersCount + meleesCount) < 6)
            {
                // Далее всех остальных
                if (buildersCount == 0) MustBuildType = EntityType.BuilderUnit;
                else
                {
                    var strategy = Strategy.First(it => it.MaxUnits > buildersCount);
                    MustBuildType = (((archersCount + meleesCount) / buildersCount) < strategy.Ratio) ? EntityType.MeleeUnit : EntityType.BuilderUnit;
                }

                if (MustBuildType == EntityType.MeleeUnit)
                {
                    if (playerView.FogOfWar)
                    {
                        MustBuildType = EntityType.RangedUnit;
                        if (HasMeleeBase)
                        {
                            if (meleesCount == 0) MustBuildType = EntityType.MeleeUnit;
                            else MustBuildType = (archersCount / meleesCount) < ARCHERSxMELEES ? EntityType.RangedUnit : EntityType.MeleeUnit;
                        }
                    }
                    else
                    {
                        if (meleesCount == 0) MustBuildType = EntityType.MeleeUnit;
                        else MustBuildType = (archersCount / meleesCount) < ARCHERSxMELEES ? EntityType.RangedUnit : EntityType.MeleeUnit;
                    }
                }

            }

            //var logObj = new PlayerView
            //{
            //    MyId = playerView.MyId,
            //    MapSize = playerView.MapSize,
            //    FogOfWar = playerView.FogOfWar,
            //    EntityProperties = playerView.EntityProperties,
            //    MaxTickCount = playerView.MaxTickCount,
            //    MaxPathfindNodes = playerView.MaxPathfindNodes,
            //    CurrentTick = playerView.CurrentTick,
            //    Players = playerView.Players,
            //    // исключаем ресурсы
            //    Entities = playerView.Entities.Where(it => it.PlayerId != null).ToArray()
            //};

            //File.AppendAllText("./log.txt", $"\r\n{JsonSerializer.Serialize(logObj)}\r\n\r\n\r\n");


            result = new Action(new System.Collections.Generic.Dictionary<int, Model.EntityAction>(0));
            foreach (var entity in myEntities)
            {
                result.EntityActions[entity.Id] = CreateAction(entity);
                // result.EntityActions.Add(entity.Id, MakeAction(entity));
            }

            bool buildRet = false;
            if (MustBuildRangedBase) 
            {
                var houseProps = CurrentView.EntityProperties[EntityType.RangedBase];
                buildRet = SearchPlaceAndBuildBase(houseProps, EntityType.RangedBase);
            }
            if (!buildRet && MustBuildMeleeBase)
            {
                var houseProps = CurrentView.EntityProperties[EntityType.MeleeBase];
                buildRet = SearchPlaceAndBuildBase(houseProps, EntityType.MeleeBase);
            }
            if (!buildRet && MustBuildHouse)
            {
                var houseProps = CurrentView.EntityProperties[EntityType.House];

                if (!Dangers.Any() || MyPlayer.Resource > (houseProps.InitialCost + ((MustBuildMeleeBase || MustBuildRangedBase) ? HIGH_NUMBER_RESOURCES_FOR_BASES : HIGH_NUMBER_RESOURCES_FOR_UNITS)))
                {
                    SearchPlaceAndBuildHouse(houseProps, EntityType.House);
                }
            }

            GetRepairBuildings();

            //if (CurrentView.CurrentTick < 800)
            //{
            //    if (HasDanger)
            //    {
            //        var dist = MyArmies.Min(it => Distance(it.Target, NearestDanger));
            //        var army = MyArmies.First(it => Distance(it.Target, NearestDanger) == dist);
            //        army.State = 1;
            //        army.Target = NearestDanger;
            //    }
            //    else
            //    {
            //        var army = MyArmies.FirstOrDefault(it => it.State != 0);
            //        if (army != null)
            //        {
            //            army.State = 0;
            //            army.Target = new Vec2Int(20, 20);
            //        }
            //    }
            //}


            MyArmies.ForEach((army) =>
            {
                army.UnitsCount = army.ExistsCall;
                army.ExistsCall = 0;

                if (CurrentView.CurrentTick < 800 || IsNoEnemies)
                {
                    if (Dangers.Any())
                    {
                        if (army.Location == ArmyPosition.Top)
                        {
                            var point = Dangers.Where(it => it.X <= 16).FirstOrDefault();
                            if (point.X > 0 && point.Y > 0)
                            {
                                army.State = ArmyState.Defender;
                                army.Target = point;
                                return;
                            }
                            else { army.State = ArmyState.Guard; }
                        }
                        if (army.Location == ArmyPosition.Right)
                        {
                            var point = Dangers.Where(it => it.Y <= 16).FirstOrDefault();
                            if (point.X > 0 && point.Y > 0)
                            {
                                army.State = ArmyState.Defender;
                                army.Target = point;
                                return;
                            }
                            else { army.State = ArmyState.Guard; }
                        }
                        if (army.Location == ArmyPosition.Diagonal)
                        {
                            var point = Dangers.Where(it => it.X > 16 && it.Y > 16).FirstOrDefault();
                            if (point.X > 0 && point.Y > 0)
                            {
                                army.State = ArmyState.Defender;
                                army.Target = point;
                                return;
                            }
                            else { 
                                army.State = ArmyState.Guard;
                                // идем на помощь 
                                var helpless = MyArmies.Where(it => it.State == ArmyState.Defender).OrderBy(it => it.UnitsCount).FirstOrDefault();
                                if (helpless != null) 
                                {
                                    army.Target = helpless.Target;
                                }
                                return;
                            }
                        }
                    }
                    else if (army.State == ArmyState.Defender) 
                    {
                        army.State = ArmyState.Guard;
                    }

                    // идем выбивать дух из противника
                    if (army.UnitsCount > GUARDIANS_ARMY_COUNT && HasMeleeBase && HasRangedBase) 
                    {
                        if (GetTarget(army)) return;
                    }

                    // охрана в мирное время
                    if (army.Location == ArmyPosition.Top)
                    {
                        var workers = MyWorkers.Where(it => it.Position.X <= 15).ToList();
                        if (workers.Any())
                        {
                            var maxY = workers.Max(it => it.Position.Y);
                            // if (maxY > army.Target.Y)
                            {
                                var worker = workers.FirstOrDefault(it => it.Position.Y == maxY);
                                maxY += GUARDIANS_FORWARD_DISTANCE;
                                if (maxY > CurrentView.MapSize - 1) maxY = CurrentView.MapSize - 1;
                                army.Target = new Vec2Int(worker.Position.X, maxY);
                            }
                        }
                    }
                    if (army.Location == ArmyPosition.Right)
                    {
                        var workers = myEntities.Where(it => IsWorker(it) && it.Position.Y <= 15).ToList();
                        if (workers.Any())
                        {
                            var maxX = workers.Max(it => it.Position.X);
                            // if (maxX > army.Target.X)
                            {
                                var worker = workers.FirstOrDefault(it => it.Position.X == maxX);
                                maxX += GUARDIANS_FORWARD_DISTANCE;
                                if (maxX > CurrentView.MapSize - 1) maxX = CurrentView.MapSize - 1;
                                army.Target = new Vec2Int(maxX, worker.Position.Y);
                            }
                        }
                    }
                    if (army.Location == ArmyPosition.Diagonal)
                    {
                        var workers = myEntities.Where(it => IsWorker(it) && it.Position.Y > 15 && it.Position.X > 15).ToList();
                        if (workers.Any())
                        {
                            var maxX = workers.Max(it => it.Position.X);
                            var maxY = workers.Max(it => it.Position.Y);
                            // if (maxX > army.Target.X || maxY > army.Target.Y)
                            {
                                maxY += GUARDIANS_FORWARD_DISTANCE;
                                if (maxY > CurrentView.MapSize - 1) maxY = CurrentView.MapSize - 1;
                                maxX += GUARDIANS_FORWARD_DISTANCE;
                                if (maxX > CurrentView.MapSize - 1) maxX = CurrentView.MapSize - 1;
                                army.Target = new Vec2Int(maxX, maxY);
                            }
                        }
                    }
                    return;
                }
                if (CurrentView.CurrentTick == 800) {

                    if (!TopEnemy.Destroyed) TopEnemy.IsTarget = true;
                    if (!RightEnemy.Destroyed) RightEnemy.IsTarget = true;
                    if (!DiagonalEnemy.Destroyed) DiagonalEnemy.IsTarget = true;

                    GetTarget(army);
                    army.State = ArmyState.Attack;
                }
                if (CurrentView.CurrentTick > 800 && army.IsOnPosition)
                {
                    GetTarget(army);
                }
            });

            return result;
        }


        private EntityAction CreateAction(Entity entity)
        {
            var moveAct = GetMove(entity);
            if (moveAct?.HasDanger ?? false)
                return new EntityAction(moveAct?.Action, null, null, null);
            if (moveAct?.HealBuilding > 0)
                return new EntityAction(moveAct?.Action, null, null, new RepairAction { Target = moveAct.HealBuilding });
            return new EntityAction(moveAct?.Action, GetBuild(entity), GetAttack(entity), null);
        }


        private MyMoveAction GetMove(Entity entity)
        {
            if (IsSoldier(entity))
            {
                Unit unitOpts = null;

                // определяем принадлежность к группе
                if (MyUnits.TryGetValue(entity.Id, out unitOpts))
                {
                    unitOpts.Army.ExistsCall += 1;
                }
                else
                {
                    var army = MyArmies.Where(it => it.State == ArmyState.Defender).OrderBy(it => Distance(CenterBase, it.Target)).FirstOrDefault();
                    if (army == null) army = MyArmies.Where(it => it.State == 0 && it.UnitsCount < GUARDIANS_ARMY_COUNT).FirstOrDefault();
                    if (army == null) army = MyArmies.FirstOrDefault(it => it.State == ArmyState.Attack);
                    if (army == null) army = MyArmies.FirstOrDefault(it => it.UnitsCount <= MyArmies.Min(it => it.UnitsCount));

                    unitOpts = new Unit
                    {
                        Army = army,
                        PrevPosition = new Vec2Int(0, 0)
                    };

                    unitOpts.Army.ExistsCall += 1;
                    MyUnits.Add(entity.Id, unitOpts);
                }

                if (IsEqual(entity.Position, unitOpts.Army.Target))
                {
                    unitOpts.Army.IsOnPosition = true;
                }

                unitOpts.PrevPosition = entity.Position;
                return new MyMoveAction
                {
                    Action = new MoveAction
                    {
                        Target = unitOpts.Army.Target,
                        FindClosestPosition = true,
                        BreakThrough = true
                    }
                };
            }


            if (IsWorker(entity))
            {
                Builder builderOpts = null;

                var dangerMoveDistance = 8;
                var danger = Dangers.FirstOrDefault(it => Distance(entity.Position, it) <= dangerMoveDistance);
                if (danger.X > 0 || danger.Y > 0)
                {
                    var newPosX = entity.Position.X;
                    if (danger.X > entity.Position.X && entity.Position.X > 0 && GlobalMap[entity.Position.X - 1, entity.Position.Y].IsEmpty)
                    {
                        newPosX -= 1;
                    }
                    else if (danger.X < entity.Position.X && entity.Position.X < CurrentView.MapSize - 1 && GlobalMap[entity.Position.X + 1, entity.Position.Y].IsEmpty)
                    {
                        newPosX += 1;
                    }

                    var newPosY = entity.Position.Y;
                    if (danger.Y > entity.Position.Y && entity.Position.Y > 0 && GlobalMap[entity.Position.X, entity.Position.Y - 1].IsEmpty)
                    {
                        newPosY -= 1;
                    }
                    else if (danger.Y < entity.Position.Y && entity.Position.Y < CurrentView.MapSize - 1 && GlobalMap[entity.Position.X, entity.Position.Y + 1].IsEmpty)
                    {
                        newPosY += 1;
                    }

                    return new MyMoveAction
                    {
                        HasDanger = true,
                        Action = new MoveAction
                        {
                            Target = new Vec2Int(newPosX, newPosY),
                            FindClosestPosition = false,
                            BreakThrough = false
                        }
                    };
                }

                // определяем принадлежность к группе
                if (!MyBuilders.TryGetValue(entity.Id, out builderOpts))
                {
                    builderOpts = new Builder
                    {
                        PrevPosition = entity.Position
                    };
                    MyBuilders.Add(entity.Id, builderOpts);
                }

                if (builderOpts.HealingBuilding > 0)
                {
                    if (MyWoundedBuildings.Any(it => it.Id == builderOpts.HealingBuilding))
                    {
                        return new MyMoveAction
                        {
                            Action = builderOpts.PrevAction,
                            HealBuilding = builderOpts.HealingBuilding
                        };
                    }
                    else 
                    {
                        builderOpts.HealingBuilding = 0;
                        builderOpts.NeedSearching = true;
                    }
                }

                if (!HasResources) return null;

                if (!IsEqual(entity.Position, builderOpts.PrevPosition)
                    || ((entity.Position.X + 1) < CurrentView.MapSize && GlobalMap[entity.Position.X + 1, entity.Position.Y].HasResource)
                    || (entity.Position.X > 0 && GlobalMap[entity.Position.X - 1, entity.Position.Y].HasResource)
                    || ((entity.Position.Y + 1) < CurrentView.MapSize && GlobalMap[entity.Position.X, entity.Position.Y + 1].HasResource)
                    || (entity.Position.Y > 0 && GlobalMap[entity.Position.X, entity.Position.Y - 1].HasResource))
                {
                    builderOpts.NeedSearching = false;
                    builderOpts.PrevPosition = entity.Position;
                    return new MyMoveAction { Action = builderOpts.PrevAction };
                }

                if (!NearestResourses.Any()) return null;

                // NeedSearching
                builderOpts.PrevAction = new MoveAction
                {
                    Target = NearestResourses[rnd.Next(0, NearestResourses.Count - 1)],
                    FindClosestPosition = true,
                    BreakThrough = true
                };

                return new MyMoveAction
                {
                    Action = builderOpts.PrevAction
                };
            }

            return null;
        }


        public AttackAction? GetAttack(Entity entity)
        {
            var properties = CurrentView.EntityProperties[entity.EntityType];

            return new AttackAction
            {
                Target = null,
                // авто атака всем
                AutoAttack = new AutoAttack
                {
                    PathfindRange = properties.SightRange,
                    // но рабочих натравливают только на ресурсы
                    ValidTargets = (entity.EntityType == EntityType.BuilderUnit) ? new EntityType[] { EntityType.Resource } : new EntityType[0]
                }
            };
        }


        public BuildAction? GetBuild(Entity entity)
        {
            if (IsWorker(entity))
            {
                // строить будем в другом месте
                return null;
            }

            var properties = CurrentView.EntityProperties[entity.EntityType];
            if (properties.Build == null)
            {
                return null;
            }

            if (AllTroops >= 110) return null;

            var buildEntityType = properties.Build.Value.Options[0];
            if (buildEntityType == MustBuildType || (buildEntityType != EntityType.BuilderUnit && MyPlayer.Resource >= HIGH_NUMBER_RESOURCES_FOR_UNITS))
            {
                return new BuildAction
                {
                    EntityType = buildEntityType,
                    Position = GetBuildInitPosition(entity, properties, buildEntityType)
                };
            }

            return null;
        }


        private Vec2Int GetBuildInitPosition(Entity entity, EntityProperties properties, EntityType buildType)
        {
            if (buildType == EntityType.BuilderUnit)
            {
                if (CurrentView.FogOfWar && MyWorkers.Count < 6)
                {
                    return GetClosestEntrance(ref entity, ref properties, MyWorkers.FirstOrDefault().Position);
                }
                return GetClosestEntrance(ref entity, ref properties, NearestResourses.FirstOrDefault());
            }
            return new Vec2Int { X = entity.Position.X + properties.Size, Y = entity.Position.Y + properties.Size - 1 };
        }


        private Vec2Int GetClosestEntrance(ref Entity entity, ref EntityProperties properties, Vec2Int targetPosition)
        {
            var posX = (targetPosition.X < entity.Position.X) ? entity.Position.X - 1 : targetPosition.X;
            if (targetPosition.X > entity.Position.X + properties.Size) posX = entity.Position.X + properties.Size;
            if (posX >= entity.Position.X && posX < entity.Position.X + properties.Size)
            {
                var posY = (targetPosition.Y < entity.Position.Y) ? entity.Position.Y - 1 : targetPosition.Y;
                if (targetPosition.Y > entity.Position.Y + properties.Size) posY = entity.Position.Y + properties.Size;
                return new Vec2Int(posX, posY);
            }
            else
            {
                var posY = (targetPosition.Y < entity.Position.Y) ? entity.Position.Y : targetPosition.Y;
                if (targetPosition.Y > entity.Position.Y + properties.Size) posY = entity.Position.Y + properties.Size - 1;
                return new Vec2Int(posX, posY);
            }
        }



        private void GetRepairBuildings()
        {
            if (!MyWoundedBuildings.Any()) return;

            List<Entity> doctors = new List<Entity>();
            var wounded = MyWoundedBuildings.Where(it => !HealingBuildings.Any(b => b == it.Id)).OrderBy(it => it.IsUnitBase).ToList();
            foreach (var building in wounded)
            {
                int doctorsCount = building.IsUnitBase ? 10 : 4;
                var myWorkers = MyWorkers.Except(doctors).OrderBy(it => Distance(it.Position, building.CenterPosition)).Take(doctorsCount).ToList();
                foreach (var worker in myWorkers)
                {
                    if (MyBuilders.TryGetValue(worker.Id, out var builderOpts))
                    {
                        builderOpts.HealingBuilding = building.Id;
                    }

                    result.EntityActions[worker.Id] = new EntityAction(new MoveAction { 
                        Target = building.CenterPosition,
                        FindClosestPosition = false,
                        BreakThrough = true
                    }, null, null, new RepairAction { Target = building.Id });
                }
                doctors.AddRange(myWorkers);
                HealingBuildings.Add(building.Id);
            }
        }


        private bool SearchPlaceAndBuildBase(EntityProperties houseProps, EntityType houseType)
        {
            // если хватает денег пытаемся построить дом
            if (MyPlayer.Resource >= houseProps.InitialCost)
            {
                var buildHouseAction = GetBuildBase(houseProps, houseType);
                if (buildHouseAction.HasValue)
                { 
                    var worker = MyWorkers.OrderBy(it => Distance(it.Position, 
                        new Vec2Int(buildHouseAction.Value.Position.X + houseProps.Size/2, buildHouseAction.Value.Position.Y + houseProps.Size / 2))).FirstOrDefault();
                    if (worker.Id > 0)
                    {
                        result.EntityActions[worker.Id] = new EntityAction(new MoveAction(buildHouseAction.Value.Position, true, false), buildHouseAction, null, null);
                        return true;
                    }
                }
            }

            return false;
        }


        private bool SearchPlaceAndBuildHouse(EntityProperties houseProps, EntityType houseType)
        {
            // если хватает денег пытаемся построить дом
            if (MyPlayer.Resource >= houseProps.InitialCost)
            {
                var myWorkers = MyWorkers.OrderBy(it => Distance(it.Position, new Vec2Int(0, 0))).ToList();
                if (myWorkers.Any())
                {
                    foreach (var worker in myWorkers)
                    {
                        var buildHouseAction = GetBuildHouse(worker, houseProps, houseType);
                        if (buildHouseAction != null)
                        {
                            result.EntityActions[worker.Id] = new EntityAction(null, buildHouseAction, null, null);
                            return true;
                        }
                    }
                }
            }

            return false;
        }


        private BuildAction? GetBuildHouse(Entity worker, EntityProperties houseProps, EntityType houseType) 
        {
            for (var i = 0; i < houseProps.Size; ++i)
            {
                var pos = new Vec2Int[4] {
                                    new Vec2Int(worker.Position.X + i - 2, worker.Position.Y + 1),
                                    new Vec2Int(worker.Position.X + 1, worker.Position.Y - i),
                                    new Vec2Int(worker.Position.X + i - 2, worker.Position.Y - houseProps.Size),
                                    new Vec2Int(worker.Position.X - houseProps.Size, worker.Position.Y - i)
                                };

                if (pos.Any(it => HasPlace(houseProps.Size, it, HasMeleeBase && HasRangedBase)))
                {
                    return new BuildAction
                    {
                        EntityType = houseType,
                        Position = pos.First(it => HasPlace(houseProps.Size, it, HasMeleeBase && HasRangedBase))
                    };
                }
            }
            return null;
        }

        private BuildAction? GetBuildBase(EntityProperties houseProps, EntityType houseType)
        {
            if (HasPlace(houseProps.Size, new Vec2Int(5, 11))) {
                return new BuildAction
                {
                    EntityType = houseType,
                    Position = new Vec2Int(5, 11)
                };
            }
            if (HasPlace(houseProps.Size, new Vec2Int(11, 5)))
            {
                return new BuildAction
                {
                    EntityType = houseType,
                    Position = new Vec2Int(11, 5)
                };
            }
            return null;
        }


        public void DebugUpdate(PlayerView playerView, DebugInterface debugInterface)
        {
            debugInterface.Send(new DebugCommand.Clear());
            debugInterface.GetState();
        }


        /// <summary>
        /// Управлямый корабль
        /// </summary>
        public bool IsSoldier(Entity entity) {
            return (entity.EntityType == EntityType.MeleeUnit
                || entity.EntityType == EntityType.RangedUnit);
        }

        public bool IsWorker(Entity entity)
        {
            return (entity.EntityType == EntityType.BuilderUnit);
        }

        public bool IsPopulateProvider(Entity entity)
        {
            return (entity.EntityType == EntityType.RangedBase
                || entity.EntityType == EntityType.MeleeBase
                || entity.EntityType == EntityType.BuilderBase
                || entity.EntityType == EntityType.House);
        }

        public bool IsBuilding(Entity entity)
        {
            return (entity.EntityType == EntityType.RangedBase
                || entity.EntityType == EntityType.MeleeBase
                || entity.EntityType == EntityType.BuilderBase
                || entity.EntityType == EntityType.House
                || entity.EntityType == EntityType.Turret);
        }



        /// <summary>
        /// Расстояние между двумя точками
        /// </summary>
        static int Distance(Vec2Int a, Vec2Int b)
        {
            return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
        }


        /// <summary>
        /// проверка на идентичность точек
        /// </summary>
        static bool IsEqual(Vec2Int a, Vec2Int? b)
        {
            if (!b.HasValue) return false;
            return (a.X == b.Value.X) && (a.Y == b.Value.Y);
        }


        static bool Contains(Vec2Int pos, Vec2Int pos2, Vec2Int point)
        {
            // Vec2Int pos2 = new Vec2Int(pos.X + size - 1, pos.Y + size - 1);
            return (point.X <= pos2.X && point.X >= pos.X && point.Y <= pos2.Y && point.Y >= pos.Y);
        }


        public bool HasPlace(int size, Vec2Int pos, bool HasBases = true)
        {
            if (pos.X < 0 || pos.Y < 0) return false;
            if (pos.X >= CurrentView.MapSize || pos.Y >= CurrentView.MapSize) return false;

            for (var i = 0; i < size; ++i)
            {
                for (var j = 0; j < size; ++j)
                {
                    if ((pos.X + i) >= CurrentView.MapSize || (pos.Y + j) >= CurrentView.MapSize)
                        return false;

                    if (!HasBases && (pos.X + i) > 3 && (pos.X + i) < 11 && (pos.Y + j) > 10 && (pos.Y + j) < 18)
                        return false;
                    if (!HasBases && (pos.X + i) > 10 && (pos.X + i) < 18 && (pos.Y + j) > 3 && (pos.Y + j) < 11)
                        return false;

                    var field = GlobalMap[pos.X + i, pos.Y + j];
                    if (field.HasBuilder || field.HasBuilding || field.HasResource || field.HasSoldier)
                        return false;
                }
            }
            return true;
        }


        private bool GetTarget(Army army)
        {
            List<Enemy> enemies;
            if (army.Location == ArmyPosition.Top) enemies = new List<Enemy> { TopEnemy, RightEnemy, DiagonalEnemy };
            else if (army.Location == ArmyPosition.Right) enemies = new List<Enemy> { RightEnemy, TopEnemy, DiagonalEnemy };
            else enemies = new List<Enemy> { DiagonalEnemy, TopEnemy, RightEnemy };

            var enemy = enemies.FirstOrDefault(it => !it.Destroyed);
            if (enemy == null)
            {
                IsNoEnemies = true;
                return false;
            }
            if (!enemy.IsTarget)
            {
                // если мало - охраняем
                if (army.UnitsCount < FULL_ARMY_COUNT) return false;

                else if (Distance(army.Target, enemy.Position) > 24)
                {
                    // если много, но далеко - подкрадываемся
                    if (army.Location == ArmyPosition.Top && army.Target.Y < enemy.Position.Y) army.Target.Y += 1;
                    if (army.Location == ArmyPosition.Right && army.Target.X < enemy.Position.X) army.Target.X += 1;
                    return false;
                }
                else
                {
                    // добиваем!!!
                    enemy.IsTarget = true;
                }
            }

            if (MyArmies.Any(it => it.State == ArmyState.Defender))
            {
                // переходим в глухую оборону
                army.State = ArmyState.Guard;
                return false;
            }
            else 
            {
                army.State = ArmyState.Attack;
            }

            if (army.IsOnPosition && IsEqual(army.Target, enemy.Position))
            {
                if (!CurrentView.Entities.Any(it => it.PlayerId != null && it.PlayerId != CurrentView.MyId && Distance(army.Target, it.Position) < 10))
                {
                    army.IsOnPosition = false;
                    enemy.Destroyed = true;
                    enemy.IsTarget = false;

                    enemy = enemies.FirstOrDefault(it => !it.Destroyed);
                    if (enemy != null)
                    {
                        army.Target = enemy.Position;
                        army.State = ArmyState.Attack;
                        enemy.IsTarget = true;
                    }
                }
            }
            else 
            {
                army.Target = enemy.Position;
                army.State = ArmyState.Attack;
                army.IsOnPosition = false;
            }
            return true;
        }



        class Unit
        {
            public Army Army;

            public Vec2Int PrevPosition;
        }


        class Army
        {
            int _index = 0;
            Vec2Int _target;

            // перекличка
            public int ExistsCall;

            public int UnitsCount;

            public ArmyState State;

            public ArmyPosition Location;

            public bool IsOnPosition;

            public Vec2Int Target;
        }


        class Builder
        {
            public bool NeedSearching;

            public Vec2Int PrevPosition;

            public MoveAction? PrevAction;

            public int HealingBuilding;

            public bool JustBuild;
        }

        class Enemy
        {
            public int Id;

            public Vec2Int Position;

            public bool Destroyed;

            public bool IsTarget;
        }


        struct Building
        {
            public int Delta;
            public Vec2Int CenterPosition;
            public int Id;
            public bool IsUnitBase;
        }


        struct Field
        {
            public bool HasResource;
            public bool HasBuilding;
            public bool HasSoldier;
            public bool HasBuilder;
            public bool IsMine;
            public bool IsEmpty {
                get { return !HasResource && !HasBuilding && !HasSoldier && !HasBuilder; }
            }
        }


        class MyRepairAction
        {
            public RepairAction Action;
            public Vec2Int Position;
        }

        class MyMoveAction
        {
            public MoveAction? Action;
            public bool HasDanger;
            public int HealBuilding;
        }


        enum ArmyPosition
        {
            Top = 0,
            Right = 1,
            Diagonal = 2
        }


        enum ArmyState
        { 
            Guard = 0,
            Defender = 1,
            Attack = 2
        }

        struct RiseStrategy 
        {
            public RiseStrategy(int m, double r)
            {
                MaxUnits = m;
                Ratio = r;
            }

            public int MaxUnits;
            public double Ratio;
        }
    }
}