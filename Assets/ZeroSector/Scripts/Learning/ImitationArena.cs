using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace ZeroSector.Learning
{
    public sealed class ImitationArena : MonoBehaviour
    {
        public CharacterController player, doubleBody;
        public Camera eyes;
        public Transform doubleVisual;
        public Material laserMaterial;
        public ArenaHud hud;
        public FirstPersonPresentation presentation;
        public string locationName="КОНТАКТ 01";
        TacticalPolicy policy, initial;
        readonly List<TacticalPolicy.Example> training = new(), validation = new();
        readonly float[] probabilities = new float[TacticalPolicy.Outputs];
        readonly float[] botProbabilities = new float[TacticalPolicy.Outputs];
        readonly System.Random random = new(180);
        readonly int[] counts = new int[5];
        readonly int[] powerCounts = new int[3];
        readonly string[] moves = { "ПОЗИЦИЯ", "СБЛИЖЕНИЕ", "ОТСТУПЛЕНИЕ", "ОБХОД СЛЕВА", "ОБХОД СПРАВА" };
        readonly string[] powers = { "ОРУЖИЕ", "ЩИТЫ", "ДВИГАТЕЛИ" };
        readonly Fighter human = new(), copy = new();
        bool captured, learning = true, roundOver;
        float pitch, sampleTimer, decisionTimer, metricsTimer, teachingTime, stanceTimer, stanceDemoWindow;
        int botMove, samples, hits, rounds, fireSamples, ignoredIdle, crouchSamples;
        bool botFire, botCrouch, lastHumanCrouch;
        float returnFireTimer;
        int underFireSamples;
        readonly CombatCounters trainingHuman = new(), trainingEnemy = new(), duelHuman = new(), duelEnemy = new();
        readonly List<DecisionRecord> decisions = new();
        string note = "F1 — обучение. Покажи стиль 30–60 секунд, затем F2 — проверка.";
        string metrics = "Контрольные примеры появятся после начала записи.";
        float[] lastBotState;
        CommanderCrouch crouch, doubleCrouch;
        Vector3 standingVisualScale;
        float standingDoubleHeight;

        [Serializable] public sealed class CombatCounters
        {
            public int shots, hits, crouchEntries;
        }

        [Serializable] sealed class DecisionRecord
        {
            public float seconds; public int move, power, updates;
            public bool fire, actualCrouchBeforeDecision; public float[] state, probabilities;
        }

        CombatCounters HumanCounters => learning ? trainingHuman : duelHuman;
        CombatCounters EnemyCounters => learning ? trainingEnemy : duelEnemy;

        sealed class Fighter
        {
            public float health = 100, shield = 60, heat, cooldown, sinceHit = 10;
            public int power = 1;
            public void Reset() { health = 100; shield = 60; heat = cooldown = 0; sinceHit = 10; power = 1; }
            public void Tick(float dt)
            {
                cooldown -= dt; sinceHit += dt;
                heat = Mathf.Max(0, heat - dt * 20);
                if (sinceHit > 3) shield = Mathf.Min(60, shield + dt * (power == 1 ? 18 : 6));
            }
            public void Damage(float amount)
            {
                sinceHit = 0;
                float absorbed = Mathf.Min(shield, amount);
                shield -= absorbed; health = Mathf.Max(0, health - (amount - absorbed));
            }
        }

        void Start()
        {
            policy = new TacticalPolicy(); initial = new TacticalPolicy();
            if(!presentation) presentation = GetComponent<FirstPersonPresentation>();
            if(!presentation) presentation = gameObject.AddComponent<FirstPersonPresentation>();
            presentation.Initialize(player,eyes);
            crouch = new CommanderCrouch(player,presentation.eyeHeight);
            doubleCrouch = new CommanderCrouch(doubleBody,null);
            standingVisualScale = doubleVisual.localScale;
            standingDoubleHeight = doubleBody.height;
            if (!hud) hud = gameObject.AddComponent<ArenaHud>();
            hud.Initialize();
            hud.SetLocation(locationName);
            eyes.GetUniversalAdditionalCameraData().antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            eyes.GetUniversalAdditionalCameraData().antialiasingQuality = AntialiasingQuality.High;
            eyes.allowDynamicResolution = false;
            SetPositions();
        }

        void Update()
        {
            var keyboard = Keyboard.current; var mouse = Mouse.current;
            if (keyboard == null || mouse == null) return;
            if (keyboard.escapeKey.wasPressedThisFrame) Capture(false);
            else if (mouse.leftButton.wasPressedThisFrame && !captured) Capture(true);
            if (keyboard.f1Key.wasPressedThisFrame) SetMode(true);
            if (keyboard.f2Key.wasPressedThisFrame) SetMode(false);
            if (keyboard.tabKey.wasPressedThisFrame) SetMode(!learning);
            if (keyboard.f3Key.wasPressedThisFrame) ResetNetwork();
            if (keyboard.f4Key.wasPressedThisFrame) ResetRound();
            if (keyboard.f5Key.wasPressedThisFrame) Save();
            if (keyboard.f9Key.wasPressedThisFrame) Load();
            if (keyboard.f6Key.wasPressedThisFrame) ExportReport();
            if(keyboard.hKey.wasPressedThisFrame) hud.ToggleDiagnostics();
            if (!captured || roundOver)
            {
                presentation.SetInput(pitch,Vector2.zero,0,false,crouch.IsCrouching,false,false,false);
                RefreshHud(); return;
            }
            float dt = Mathf.Min(Time.deltaTime, .05f);
            Vector2 look = mouse.delta.ReadValue() * .085f;
            player.transform.Rotate(0, look.x, 0);
            pitch = Mathf.Clamp(pitch - look.y, -78, 78);
            presentation.ApplyLook(pitch);
            crouch.Tick(CommanderCrouch.ReadInput(keyboard), dt);
            if(crouch.IsCrouching != lastHumanCrouch)
            { stanceDemoWindow = .8f; if(crouch.IsCrouching) HumanCounters.crouchEntries++; }
            lastHumanCrouch = crouch.IsCrouching;
            stanceDemoWindow = Mathf.Max(0,stanceDemoWindow-dt);
            human.Tick(dt); copy.Tick(dt);
            if (keyboard.digit1Key.wasPressedThisFrame) human.power = 0;
            if (keyboard.digit2Key.wasPressedThisFrame) human.power = 1;
            if (keyboard.digit3Key.wasPressedThisFrame) human.power = 2;
            float x = (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0);
            float z = (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0);
            Vector3 direction = player.transform.TransformDirection(Vector3.ClampMagnitude(new Vector3(x, 0, z), 1));
            bool aiming = mouse.rightButton.isPressed;
            if(keyboard.qKey.wasPressedThisFrame) presentation.Equip(presentation.equippedIndex+1);
            float wheel=mouse.scroll.ReadValue().y;
            if(Mathf.Abs(wheel)>0.1f) presentation.Equip(presentation.equippedIndex+(wheel>0 ? -1 : 1));
            if(keyboard.fKey.wasPressedThisFrame && !presentation.HasWeapon)
                note="Осмотру нужна подключённая модель. Останови Play → Zero Sector → Graphics → Connect Easy FPS Preview.";
            bool sprint = keyboard.leftShiftKey.isPressed && z>0 && !aiming && !crouch.IsCrouching && !mouse.leftButton.isPressed;
            presentation.SetInput(pitch,look,direction.magnitude,sprint,crouch.IsCrouching,aiming,keyboard.fKey.wasPressedThisFrame,true);
            float moveSpeed = (human.power == 2 ? 6 : 4) * (crouch.IsCrouching ? .5f : sprint ? 1.5f : aiming ? .65f : 1);
            player.Move((direction * moveSpeed + Vector3.down * 3) * dt);
            // Inspection can be cancelled by fire without an accidental simultaneous shot.
            if(mouse.leftButton.wasPressedThisFrame && presentation.IsInspecting) presentation.ResetView();
            bool firing = mouse.leftButton.isPressed && presentation.CanFire &&
                (presentation.Equipped==null || presentation.Equipped.automatic || mouse.leftButton.wasPressedThisFrame);
            sampleTimer -= dt;
            if (learning && sampleTimer <= 0)
            {
                sampleTimer = .2f;
                // Thinking/reading must not silently teach the policy to do nothing.
                bool demonstrating = direction.sqrMagnitude > .05f || firing || keyboard.xKey.isPressed || crouch.IsCrouching || stanceDemoWindow > 0;
                if (demonstrating)
                {
                    var example = new TacticalPolicy.Example(Observe(player,doubleBody,human,copy),
                        Classify(direction,player.transform.position,doubleBody.transform.position),firing,human.power,crouch.IsCrouching);
                    if(example.State[10] > 0) underFireSamples++;
                    bool usedForTraining = Record(example);
                    if (usedForTraining && training.Count >= 12)
                    {
                        // Fixed work per new sample; training no longer depends on render FPS.
                        policy.Train(example);
                        for (int i = 0; i < 11; i++)
                        {
                            int start = random.NextDouble() < .75 ? Math.Max(0,training.Count-80) : 0;
                            policy.Train(training[random.Next(start,training.Count)]);
                        }
                    }
                }
                else ignoredIdle++;
            }
            if (firing) Shoot(true);

            if (!learning && !roundOver)
            {
                decisionTimer -= dt;
                stanceTimer -= dt;
                if (decisionTimer <= 0)
                {
                    decisionTimer = .6f;
                    lastBotState = Observe(doubleBody,player,copy,human);
                    policy.Predict(lastBotState,botProbabilities);
                    // Sample the learned distribution. No hand-written attack/route fallback.
                    botMove = TacticalPolicy.Sample(botProbabilities,0,5,random);
                    botFire = random.NextDouble() < botProbabilities[5];
                    copy.power = TacticalPolicy.Sample(botProbabilities,6,3,random);
                    if(decisions.Count==2000) decisions.RemoveAt(0);
                    decisions.Add(new DecisionRecord { seconds=Time.timeSinceLevelLoad,move=botMove,fire=botFire,
                        actualCrouchBeforeDecision=doubleCrouch.IsCrouching,power=copy.power,updates=policy.Updates,
                        state=(float[])lastBotState.Clone(), probabilities=(float[])botProbabilities.Clone() });
                }
                if(stanceTimer <= 0)
                {
                    stanceTimer = 1.2f;
                    botCrouch = random.NextDouble() < botProbabilities[9];
                }
                bool wasCrouching=doubleCrouch.IsCrouching;
                doubleCrouch.Tick(botCrouch,dt);
                if(!wasCrouching && doubleCrouch.IsCrouching) EnemyCounters.crouchEntries++;
                Vector3 visualScale = standingVisualScale;
                visualScale.y *= doubleBody.height/standingDoubleHeight;
                doubleVisual.localScale = Vector3.Lerp(doubleVisual.localScale,visualScale,1-Mathf.Exp(-14*dt));
                Vector3 forward = FlatDirection(doubleBody.transform.position, player.transform.position);
                if (forward.sqrMagnitude > .1f) doubleVisual.rotation = Quaternion.LookRotation(forward);
                Vector3 motion = Decode(botMove, forward) * (copy.power == 2 ? 6 : 4) * (doubleCrouch.IsCrouching ? .5f : 1);
                doubleBody.Move((motion + Vector3.down * 3) * dt);
                if (botFire) Shoot(false);
            }
            else if (learning)
            {
                Vector3 forward = FlatDirection(doubleBody.transform.position, player.transform.position);
                if (forward.sqrMagnitude > .1f) doubleVisual.rotation = Quaternion.LookRotation(forward);
                // Teaching opponent is explicitly scripted. Only the human's actions train the model.
                // Unlike the previous passive target, this exposes the learner to damaged states.
                returnFireTimer -= dt;
                if(returnFireTimer <= 0) { returnFireTimer=.8f; Shoot(false); }
                if (direction.sqrMagnitude > .05f || firing || keyboard.xKey.isPressed || crouch.IsCrouching || stanceDemoWindow > 0) teachingTime += dt;
                if (teachingTime >= 12)
                {
                    int power = human.power;
                    ResetRound(); human.power = power;
                    note = "Новый заход под ответным огнём. Веса и счётчики сессии сохранены.";
                }
            }
            if (player.transform.position.y < -5 || doubleBody.transform.position.y < -5) ResetRound();
            metricsTimer -= dt;
            if (metricsTimer <= 0) { metricsTimer = 1; UpdateMetrics(); }
            RefreshHud();
        }

        static Vector3 FlatDirection(Vector3 from, Vector3 to)
        {
            var direction = to - from; direction.y = 0;
            return direction.sqrMagnitude > .001f ? direction.normalized : Vector3.forward;
        }

        static int Classify(Vector3 motion, Vector3 from, Vector3 to)
        {
            if (motion.sqrMagnitude < .05f) return 0;
            Vector3 forward = FlatDirection(from, to), right = Vector3.Cross(Vector3.up, forward);
            float f = Vector3.Dot(motion, forward), r = Vector3.Dot(motion, right);
            return Mathf.Abs(f) > Mathf.Abs(r) ? (f > 0 ? 1 : 2) : (r < 0 ? 3 : 4);
        }

        static Vector3 Decode(int action, Vector3 forward)
        {
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            return action == 1 ? forward : action == 2 ? -forward : action == 3 ? -right : action == 4 ? right : Vector3.zero;
        }

        float[] Observe(CharacterController actor, CharacterController target, Fighter self, Fighter other)
        {
            Vector3 forward = FlatDirection(actor.transform.position, target.transform.position);
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 origin = actor.transform.TransformPoint(actor.center);
            bool visible = Physics.Raycast(origin, (target.transform.TransformPoint(target.center) - origin).normalized, out var hit, 40)
                && hit.collider == target;
            return new[]
            {
                Mathf.Clamp01(Vector3.Distance(actor.transform.position,target.transform.position)/30),
                self.health/100, self.shield/60, self.heat/100, other.health/100, other.shield/60,
                Clearance(origin,-right), Clearance(origin,right), Clearance(origin,forward), Clearance(origin,-forward),
                Mathf.Clamp01(1-self.sinceHit/3), visible ? 1f : 0f
            };
        }

        static float Clearance(Vector3 origin, Vector3 direction)
            => Physics.Raycast(origin, direction, out var hit, 4) ? Mathf.Clamp01(hit.distance / 4) : 1;

        bool Record(TacticalPolicy.Example example)
        {
            samples++; counts[example.Move]++; powerCounts[example.Power]++;
            if(example.Fire) fireSamples++;
            if(example.Crouch) crouchSamples++;
            // These hold-outs are within-session diagnostics, not an independent human study.
            var buffer = samples % 5 == 0 ? validation : training;
            int limit = buffer == validation ? 400 : 1600;
            if (buffer.Count == limit) buffer.RemoveAt(0);
            buffer.Add(example);
            return buffer == training;
        }

        void Shoot(bool fromHuman)
        {
            Fighter shooter = fromHuman ? human : copy;
            if (shooter.cooldown > 0 || shooter.heat > 85 || roundOver) return;
            var weapon=fromHuman ? presentation.Equipped : null;
            float interval=weapon==null ? .32f : Mathf.Max(.08f,weapon.interval);
            shooter.cooldown = interval*(shooter.power==0 ? .5625f : 1);
            shooter.heat += weapon==null ? 13 : weapon.heat;
            var counters=fromHuman ? HumanCounters : EnemyCounters;
            counters.shots++;
            bool landed=false;
            Vector3 origin = fromHuman ? eyes.transform.position : doubleBody.transform.TransformPoint(doubleBody.center + Vector3.up * (doubleBody.height * .3f));
            Vector3 direction = fromHuman ? eyes.transform.forward : (player.transform.TransformPoint(player.center) - origin).normalized;
            if (!fromHuman)
                direction = (direction + new Vector3((float)(random.NextDouble()-.5)*.045f,(float)(random.NextDouble()-.5)*.045f,0)).normalized;
            Vector3 end = origin + direction * 60;
            if (Physics.Raycast(origin, direction, out var hit, 60))
            {
                end = hit.point;
                if (hit.collider == (fromHuman ? doubleBody : player))
                {
                    var victim = fromHuman ? copy : human;
                    victim.Damage(weapon==null ? 12 : weapon.damage);
                    counters.hits++; landed=true;
                    if(!fromHuman) presentation.OnDamage();
                    if (fromHuman) hits++;
                    if (victim.health <= 0)
                    {
                        if (learning)
                        {
                            if(fromHuman) copy.Reset();
                            else { ResetRound(); note="Учебный раунд окончен. Веса сохранены; продолжай бой под огнём."; }
                        }
                        else
                        {
                            roundOver = true; rounds++;
                            note = fromHuman ? "Ты выиграл раунд. F4 — повтор с теми же весами." : "Двойник выиграл раунд. F4 — повтор с теми же весами.";
                        }
                    }
                }
            }
            if(fromHuman) presentation.OnShot(landed);
            var beam = new GameObject("Arena beam", typeof(LineRenderer));
            var line = beam.GetComponent<LineRenderer>();
            line.sharedMaterial = laserMaterial;
            line.positionCount = 2;
            line.startWidth = .018f; line.endWidth = .004f;
            line.SetPosition(0, fromHuman ? presentation.MuzzlePosition : origin + direction * .5f);
            line.SetPosition(1, end);
            line.startColor = line.endColor = fromHuman ? Color.cyan : new Color(1,.3f,.1f);
            Destroy(beam,.07f);
        }

        void SetMode(bool teach)
        {
            if (!teach && training.Count < 12 && policy.Updates == 0)
            { note = "Сначала покажи хотя бы 15 действий в F1 (примерно 3 секунды). Лучше 30–60 секунд."; return; }
            learning = teach;
            ResetRound();
            note = teach ? "Обучение под огнём. Учебный противник стреляет по фиксированному режиму; модель учится на твоих действиях." : "Проверка: веса заморожены. Движение, огонь, энергия и присед выбираются по вероятностям нейросети.";
        }

        void SetPositions()
        {
            player.enabled = doubleBody.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(0,.05f,-7),Quaternion.identity);
            doubleBody.transform.SetPositionAndRotation(new Vector3(0,.05f,0),Quaternion.Euler(0,180,0));
            player.enabled = doubleBody.enabled = true;
            pitch = 0; eyes.transform.localRotation = Quaternion.identity;
        }

        void ResetRound()
        {
            human.Reset(); copy.Reset(); roundOver = false; decisionTimer = stanceTimer = 0; botMove = 0; botFire = botCrouch = false;
            teachingTime=0;
            returnFireTimer=1.5f;
            SetPositions();
            crouch.ResetAtSafeSpawn(); doubleCrouch.ResetAtSafeSpawn();
            doubleVisual.localScale = standingVisualScale;
            lastHumanCrouch=false; stanceDemoWindow=0;
            if(presentation) presentation.ResetView();
        }

        void ResetNetwork()
        {
            policy = new TacticalPolicy(); initial = new TacticalPolicy();
            training.Clear(); validation.Clear(); Array.Clear(counts,0,counts.Length);
            Array.Clear(powerCounts,0,powerCounts.Length);
            samples = hits = fireSamples = ignoredIdle = crouchSamples = rounds = 0; learning = true;
            underFireSamples=0; decisions.Clear();
            foreach(var counter in new[]{trainingHuman,trainingEnemy,duelHuman,duelEnemy}) counter.shots=counter.hits=counter.crouchEntries=0;
            ResetRound(); UpdateMetrics();
            note = "Новая чистая модель. Старые данные удалены из памяти; сохранённый файл не изменён.";
        }

        void UpdateMetrics()
        {
            if (validation.Count < 5) { metrics = "Для метрик нужно хотя бы 25 записанных действий."; return; }
            float startLoss = 0, currentLoss = 0; int moveCorrect = 0, energyCorrect = 0, stanceCorrect = 0, stancePositive = 0;
            int[] controlCounts=new int[5];
            foreach (var example in validation)
            {
                startLoss += initial.Loss(example); currentLoss += policy.Loss(example);
                policy.Predict(example.State, probabilities);
                if (TacticalPolicy.ArgMax(probabilities,0,5)==example.Move) moveCorrect++;
                if (TacticalPolicy.ArgMax(probabilities,6,3)==example.Power) energyCorrect++;
                controlCounts[example.Move]++;
                if((probabilities[9]>=.5f)==example.Crouch) stanceCorrect++;
                if(example.Crouch) stancePositive++;
            }
            int majority=0;
            foreach(int count in controlCounts) majority=Math.Max(majority,count);
            metrics = $"Контроль: {validation.Count} примеров, НЕ использованных для обучения\n"+
                $"Ошибка: {startLoss/validation.Count:F3} → {currentLoss/validation.Count:F3}\n"+
                $"Движение: {moveCorrect*100/validation.Count}% (простая база: {majority*100/validation.Count}%)\n"+
                $"Энергия: {energyCorrect*100/validation.Count}%\n"+
                $"Присед: {stanceCorrect*100/validation.Count}% (база: {Math.Max(stancePositive,validation.Count-stancePositive)*100/validation.Count}%)\n"+
                "Это диагностика той же сессии, не независимый эксперимент.";
        }

        string SavePath => Path.Combine(Application.persistentDataPath,"DoppelgangerArena","policy-v2.json");
        void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
                string temporary = SavePath+".tmp";
                File.WriteAllText(temporary,JsonUtility.ToJson(policy.Export(),true));
                if (File.Exists(SavePath)) File.Copy(SavePath,SavePath+".bak",true);
                File.Copy(temporary,SavePath,true); File.Delete(temporary);
                note = "Веса сохранены. F9 — загрузить. Путь записан в Console.";
                Debug.Log("Arena model saved: "+SavePath);
            }
            catch(Exception exception) { note = "Не удалось сохранить: "+exception.Message; }
        }

        void Load()
        {
            try
            {
                string path=File.Exists(SavePath) ? SavePath : Path.Combine(Path.GetDirectoryName(SavePath),"policy-v1.json");
                var data = JsonUtility.FromJson<TacticalPolicy.PolicyData>(File.ReadAllText(path));
                policy.Import(data);
                training.Clear(); validation.Clear(); Array.Clear(counts,0,counts.Length); samples=0;
                Array.Clear(powerCounts,0,powerCounts.Length); fireSamples=ignoredIdle=crouchSamples=0;
                underFireSamples=0;
                learning = false; ResetRound(); UpdateMetrics();
                note = data.version == 1 ? "Старые навыки загружены. Присед ещё не обучен: Tab — покажи новые примеры." : "Загружены обученные веса. F2 — проверка; F1 — продолжить обучение.";
            }
            catch(Exception exception) { note = "Модель не загружена: "+exception.Message; }
        }

        void ExportReport()
        {
            try
            {
                UpdateMetrics();
                string folder=Path.GetDirectoryName(SavePath); Directory.CreateDirectory(folder);
                string path=Path.Combine(folder,"report-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".txt");
                File.WriteAllText(path,$"Doppelganger arena / local behavioral cloning\nUTC: {DateTime.UtcNow:O}\n"+
                    $"Architecture: 12 -> 24 tanh -> movement(5), fire(1), power(3), crouch(1)\nSamples this session: {samples}\n"+
                    $"Training buffer: {training.Count}\nSGD updates: {policy.Updates}\nMode: {(learning ? "train" : "frozen")}\n"+
                    $"Movement labels: {string.Join(",",counts)}\nPower labels: {string.Join(",",powerCounts)}\n"+
                    $"Fire labels: {fireSamples}/{samples}\nIgnored idle: {ignoredIdle}\nHits: {hits}\nRounds: {rounds}\n{metrics}\n"+
                    $"Crouch labels: {crouchSamples}/{samples}\nActual bot crouch: {doubleCrouch.IsCrouching}\n"+
                    $"Examples after taking damage: {underFireSamples}\n"+
                    $"Training opponent (scripted) shots/hits/crouches: {trainingEnemy.shots}/{trainingEnemy.hits}/{trainingEnemy.crouchEntries}\n"+
                    $"Frozen learned opponent shots/hits/crouches: {duelEnemy.shots}/{duelEnemy.hits}/{duelEnemy.crouchEntries}\n"+
                    "Validation is within-session and can contain correlated states. No claim of independent generalization.\n");
                var dump=new SessionDump { model=policy.Export(), botState=lastBotState,
                    botProbabilities=(float[])botProbabilities.Clone(), training=Dump(training), validation=Dump(validation),
                    teachingHuman=trainingHuman,teachingOpponent=trainingEnemy,frozenHuman=duelHuman,frozenOpponent=duelEnemy,
                    examplesAfterDamage=underFireSamples,decisions=decisions.ToArray() };
                File.WriteAllText(Path.ChangeExtension(path,".json"),JsonUtility.ToJson(dump,true));
                note="Отчёт сохранён. Путь — в Console."; Debug.Log("Arena report: "+path);
            }
            catch(Exception exception) { note="Ошибка отчёта: "+exception.Message; }
        }

        void RefreshHud()
        {
            if (!hud) return;
            hud.Refresh(learning,captured,roundOver,
                $"ТЫ  {human.health:F0} HP / {human.shield:F0} щит    {powers[human.power]}\nДВОЙНИК  {copy.health:F0} HP / {copy.shield:F0} щит    {powers[copy.power]}",
                $"Примеры: {samples} / обновления: {policy.Updates}\n"+
                $"Ты показал: слева {counts[3]}, справа {counts[4]}\nОгонь в {fireSamples*100/Math.Max(1,samples)}% примеров\n"+
                $"Присед в {crouchSamples*100/Math.Max(1,samples)}% примеров\n"+
                (learning ? $"Под огнём: {underFireSamples} примеров\nНовый заход через {Mathf.CeilToInt(12-teachingTime)} с" :
                $"Он выбрал: {moves[botMove]}\nОгонь: {(botFire ? "ДА" : "НЕТ")} / вероятность {botProbabilities[5]*100:F0}%\n"+
                $"Присед: {(doubleCrouch.IsCrouching ? "ДА" : "НЕТ")} / вероятность {botProbabilities[9]*100:F0}%"),
                metrics,note);
            hud.RefreshCombat(presentation,human.health,human.shield,human.heat,copy.health,copy.shield,
                HumanCounters,EnemyCounters,learning,crouch.IsCrouching,powers[human.power]+" / "+presentation.WeaponTitle);
        }

        [Serializable] sealed class RecordedExample { public float[] state; public int move,power; public bool fire,crouch; }
        [Serializable] sealed class SessionDump
        {
            public TacticalPolicy.PolicyData model;
            public float[] botState,botProbabilities;
            public RecordedExample[] training,validation;
            public CombatCounters teachingHuman,teachingOpponent,frozenHuman,frozenOpponent;
            public int examplesAfterDamage;
            public DecisionRecord[] decisions;
        }
        static RecordedExample[] Dump(List<TacticalPolicy.Example> examples)
        {
            var result=new RecordedExample[examples.Count];
            for(int i=0;i<result.Length;i++) result[i]=new RecordedExample {state=examples[i].State,move=examples[i].Move,power=examples[i].Power,fire=examples[i].Fire,crouch=examples[i].Crouch};
            return result;
        }

        void Capture(bool value)
        {
            captured=value; Cursor.lockState=value ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible=!value;
        }
        void OnDisable() { Capture(false); }
    }
}
