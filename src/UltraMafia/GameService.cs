using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using UltraMafia.DAL.Enums;
using UltraMafia.DAL.Model;
using UltraMafia.Frontend;
using UltraMafia.GameModel;
using UltraMafia.Helpers;

namespace UltraMafia
{
    public class GameService
    {
        private readonly IFrontend _frontend;
        private readonly GameSettings _gameSettings;
        private readonly int _minimalGamerCount;
        private readonly IServiceProvider _serviceProvider;
        private readonly Dictionary<int, List<int>> _healedHimselfRegistry = new Dictionary<int, List<int>>();

        public GameService(IFrontend frontend, GameSettings gameSettings, IServiceProvider serviceProvider)
        {
            _frontend = frontend;
            _gameSettings = gameSettings;
            _serviceProvider = serviceProvider;
            _minimalGamerCount = _gameSettings.MinGamerCount < 4 ? 4 : _gameSettings.MinGamerCount;
        }

        public void ListenToEvents()
        {
            _frontend.GameJoinRequest += GameJoinHandler;
            _frontend.GameCreationRequest += GameCreationHandler;
            _frontend.GameStartRequest += GameStartHandler;
            _frontend.GameStopRequest += GameStopHandler;
            _frontend.GameLeaveRequest += GameLeaveHandler;
            _frontend.ActivateFrontend();
        }

        public void CheckDatabase()
        {
            using var dbContextAccessor = _serviceProvider.GetDbContext();
            Log.Information("Cleaning up database...");
            dbContextAccessor.DbContext.Database.ExecuteSqlRaw(
                "update `GameSessions` set `State`='ForceFinished' where `State`='Playing'");
            Log.Information("Database is cleaned from old sessions");
        }

        private async void GameLeaveHandler((int roomId, int gamerId) leaveInfo)
        {
            GameSession gameSession;
            using (var dbContext = _serviceProvider.GetDbContext())
            {
                var gamerAccount = await dbContext.DbContext.GamerAccounts.FindAsync(leaveInfo.gamerId);
                var sessionMember =
                    await dbContext.DbContext.GameSessionMembers.FirstOrDefaultAsync(sm =>
                        sm.GamerAccountId == leaveInfo.gamerId &&
                        sm.GameSession.State == GameSessionStates.Registration &&
                        sm.GameSession.RoomId == leaveInfo.roomId);

                if (sessionMember == null)
                {
                    await _frontend.SendMessageToGamer(gamerAccount, "Ты не можешь выйти из игры в настоящий момент!");
                    return;
                }

                dbContext.DbContext.GameSessionMembers.Remove(sessionMember);
                await dbContext.DbContext.SaveChangesAsync();
                gameSession = await dbContext.DbContext.GameSessions
                    .Include(sm => sm.GameMembers)
                    .ThenInclude(sm => sm.GamerAccount)
                    .Include(sm => sm.CreatedByGamerAccount)
                    .Include(sm => sm.Room)
                    .FirstAsync(sm => sm.Id == sessionMember.GameSessionId);
            }

            _frontend.OnGamerLeft(gameSession);
        }

        private async void GameStopHandler((int roomId, int gamerId) stopInfo)
        {
            try
            {
                GameSession session;
                GameRoom room;
                using (var dbContextAccessor = _serviceProvider.GetDbContext())
                {
                    var (roomId, gamerId) = stopInfo;
                    room = await dbContextAccessor.DbContext.GameRooms.FindAsync(roomId);
                    session =
                        await dbContextAccessor.DbContext.GameSessions.FirstOrDefaultAsync(s =>
                            s.RoomId == roomId &&
                            !new[] {GameSessionStates.GameOver, GameSessionStates.ForceFinished}.Contains(s.State));
                    if (session == null)
                    {
                        await _frontend.SendMessageToRoom(room, "Нету игровой сессий чтобы выходить!");
                        return;
                    }

                    if (session.State == GameSessionStates.Playing)
                    {
                        await _frontend.SendMessageToRoom(room, "Нельзя так! Народ играет!");
                        return;
                    }

                    if (session.CreatedByGamerAccountId != gamerId)
                    {
                        await _frontend.SendMessageToRoom(room, "Игру может удалить только ее создатель!");
                        return;
                    }

                    dbContextAccessor.DbContext.Remove(session);
                    await dbContextAccessor.DbContext.SaveChangesAsync();
                }

                session.Room = room;
                await _frontend.SendMessageToRoom(session.Room, "Регистрация на игру остановлена!");
                _frontend.OnGameRegistrationStopped(session);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error occured when stopping game");
            }
        }

        private async void GameStartHandler(int roomId)
        {
            try
            {
                GameSession session;
                GameRoom room;
                using (var dbContextAccessor = _serviceProvider.GetDbContext())
                {
                    room = await dbContextAccessor.DbContext.GameRooms.FindAsync(roomId);
                    session =
                        await dbContextAccessor.DbContext.GameSessions
                            .FirstOrDefaultAsync(s =>
                                s.RoomId == roomId &&
                                !new[] {GameSessionStates.GameOver, GameSessionStates.ForceFinished}.Contains(s.State));
                    if (session == null)
                    {
                        await _frontend.SendMessageToRoom(room, "Нет игры которую можно начать. Сначала создайте её!");
                        return;
                    }

                    if (session.State == GameSessionStates.Playing)
                    {
                        await _frontend.SendMessageToRoom(room, "Игра уже играется) Играй чёрт тебя побери)");
                        return;
                    }

                    if (await dbContextAccessor.DbContext.GameSessionMembers.CountAsync(gm =>
                        gm.GameSessionId == session.Id) < _minimalGamerCount)
                    {
                        await _frontend.SendMessageToRoom(room,
                            $"Не хватает игроков, минимальное количество для старта: {_minimalGamerCount}");
                        return;
                    }

                    await dbContextAccessor.DbContext.Entry(session).Collection(c => c.GameMembers).LoadAsync();
                    ResolveRoles(session);
                    session.State = GameSessionStates.Playing;
                    await dbContextAccessor.DbContext.SaveChangesAsync();
                }

                session.Room = room;
                await _frontend.SendMessageToRoom(session.Room,
                    $"Игра начинается! Количество игроков мафиози: {session.GameMembers.Count(m => m.Role == GameRoles.Mafia)}");
                _frontend.OnGameStarted(session);

                RunGame(session.Id);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error occured when starting a game");
            }
        }

        private async void GameCreationHandler((int roomId, int gamerId) creationInfo)
        {
            try
            {
                GameSession createdSession;
                using (var dbContextAccessor = _serviceProvider.GetDbContext())
                {
                    // trying to find existing session
                    var sessionExists = await dbContextAccessor.DbContext.GameSessions
                        .AnyAsync(i =>
                            i.RoomId == creationInfo.roomId &&
                            new[] {GameSessionStates.Playing, GameSessionStates.Registration}.Contains(i.State));
                    if (sessionExists)
                    {
                        var room = await dbContextAccessor.DbContext.GameRooms.FindAsync(creationInfo.roomId);
                        await _frontend.SendMessageToRoom(room, "Нельзя создавать игру там где она уже есть :)");
                        return;
                    }

                    createdSession = new GameSession
                    {
                        RoomId = creationInfo.roomId,
                        State = GameSessionStates.Registration,
                        CreatedByGamerAccountId = creationInfo.gamerId
                    };
                    await dbContextAccessor.DbContext.AddAsync(createdSession);
                    await dbContextAccessor.DbContext.SaveChangesAsync();
                    await dbContextAccessor.DbContext.Entry(createdSession)
                        .Reference(m => m.Room).LoadAsync();
                    await dbContextAccessor.DbContext.Entry(createdSession)
                        .Reference(m => m.CreatedByGamerAccount).LoadAsync();
                }

                _frontend.OnGameSessionCreated(createdSession);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error occured when creating a game");
            }
        }

        private async void GameJoinHandler((int roomId, int gamerId) joinInfo)
        {
            try
            {
                GameSession currentSession;
                GamerAccount joinedGamerAccount;
                using (var dbContextAccessor = _serviceProvider.GetDbContext())
                {
                    joinedGamerAccount = await dbContextAccessor.DbContext.GamerAccounts.FindAsync(joinInfo.gamerId);
                    currentSession = await dbContextAccessor.DbContext.GameSessions
                        .Include(s => s.GameMembers)
                        .Include(s => s.Room)
                        .FirstOrDefaultAsync(g =>
                            g.RoomId == joinInfo.roomId &&
                            !new[] {GameSessionStates.GameOver, GameSessionStates.ForceFinished}.Contains(g.State));
                    if (currentSession == null)
                    {
                        await _frontend.SendMessageToGamer(joinedGamerAccount,
                            "Игры в данной комнате не существует. Необходимо создать ее.");
                        return;
                    }

                    if (currentSession.State != GameSessionStates.Registration)
                    {
                        await _frontend.SendMessageToGamer(joinedGamerAccount,
                            "Нельзя зарегистрироваться, игра уже идет");
                        return;
                    }

                    if (!_gameSettings.DevelopmentMode &&
                        currentSession.GameMembers.Any(gm => gm.GamerAccountId == joinInfo.gamerId))
                    {
                        await _frontend.SendMessageToGamer(joinedGamerAccount, "Да ты уже в игре! Жди :)");
                        return;
                    }

                    currentSession.GameMembers.Add(new GameSessionMember
                    {
                        GamerAccountId = joinInfo.gamerId,
                        GameSessionId = currentSession.Id
                    });
                    await dbContextAccessor.DbContext.SaveChangesAsync();

                    await dbContextAccessor.DbContext.Entry(currentSession).Collection(s => s.GameMembers).Query()
                        .Include(gm => gm.GamerAccount).LoadAsync();
                    await dbContextAccessor.DbContext.Entry(currentSession).Reference(s => s.CreatedByGamerAccount)
                        .LoadAsync();
                }

                _frontend.OnGamerJoined(currentSession, joinedGamerAccount);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error occured when user tried to join a game");
            }
        }

        private async void RunGame(int sessionId)
        {
            try
            {
                // creating doctor healing registry for session
                _healedHimselfRegistry.Add(sessionId, new List<int>());

                using var dbContextAccessor = _serviceProvider.GetDbContext();
                var session = dbContextAccessor.DbContext
                    .GameSessions
                    .Include(g => g.Room)
                    .Include("GameMembers.GamerAccount")
                    .First(s => s.Id == sessionId);
                // game timer
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                await SendIntroduceMessages(session.GameMembers);
                var dayNumber = 1;
                while (true)
                {
                    #region Night logic

                    var nightKillsCount = 0;
                    await _frontend.SendMessageToRoom(session.Room,
                        $@"
<b>Ночь #{dayNumber}</b> 🌃  
На улицах очень тихо, но это пока.

<b>Игроки</b>: 
{session.GetMembersInfo(false, true)}
", true);
                    await Task.Delay(3000);
                    var doctorActionTask = AskDoctorForAction(session).ConfigureAwait(false);
                    await Task.Delay(2000);
                    var mafiaActionTask = AskMafiaForAction(session).ConfigureAwait(false);
                    await Task.Delay(2000);
                    var copActionTask = AskCopForAction(session).ConfigureAwait(false);

                    // resolving actions
                    var doctorAction = await doctorActionTask;
                    GameSessionMember healingTarget = null;
                    if (doctorAction.Action != null)
                    {
                        healingTarget = doctorAction.Target;
                        await HealGamer(healingTarget);
                    }

                    var copAction = await copActionTask;
                    switch (copAction.Action)
                    {
                        case GameActions.Killing:
                            if (await KillGamer(session, GameRoles.Cop, copAction.Target, healingTarget))
                            {
                                nightKillsCount++;
                            }

                            break;
                        case GameActions.Checkup:
                            await InspectGamer(session, GameRoles.Cop, copAction.Target);
                            break;
                        case null:
                            // nothing to do, maybe cop is dead? (or he sleeps)
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    var mafiaAction = await mafiaActionTask;
                    switch (mafiaAction.Action)
                    {
                        case GameActions.Killing:
                            if (await KillGamer(session, GameRoles.Mafia, mafiaAction.Target, healingTarget))
                            {
                                nightKillsCount++;
                            }

                            break;
                        case GameActions.Checkup:
                            await InspectGamer(session, GameRoles.Mafia, mafiaAction.Target);
                            break;
                        case null:
                            // nothing to do, maybe mafia sleeps?
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    if (nightKillsCount == 0)
                    {
                        await _frontend.SendMessageToRoom(session.Room, "Удивительно. Все остались живы.");
                    }

                    #endregion

                    await Task.Delay(3000);
                    // ensure this game is over
                    if (await IsGameOver(session, stopwatch))
                        break;

                    #region Day logic

                    await _frontend.SendMessageToRoom(session.Room,
                        $@"
День #{dayNumber} ☀️
Все проснулись под пение птичек. 
Пришло время наказать мафию.

<b>Игроки</b>: 
{session.GetMembersInfo(false, true)}

А теперь давайте обсудим прошедшую ночь, затем будем голосовать. 
Время на обсуждение: 1 минута 30 секунд.
", true);

                    await Task.Delay(_gameSettings.DevelopmentMode ? 2000 : 90000);
                    var gamerForLynch = await PublicLynchVote(session);

                    if (gamerForLynch != null)
                    {
                        var lynchApproved = await ApproveLynch(session, gamerForLynch);
                        if (lynchApproved)
                        {
                            await _frontend.SendMessageToRoom(session.Room,
                                @$"Вешаем {gamerForLynch.GamerAccount.NickName}...");
                            await KillGamer(session, GameRoles.Citizen, gamerForLynch, null);
                        }
                        else
                        {
                            await _frontend.SendMessageToRoom(session.Room,
                                $@"Это удивительно, {gamerForLynch.GamerAccount.NickName} был на волоске от смерти. 
Граждане решили предоставить ему шанс.");
                        }
                    }
                    else
                    {
                        await _frontend.SendMessageToRoom(session.Room,
                            "Мнения жителей разошлись. Никого не будем вешать.");
                    }

                    #endregion

                    await Task.Delay(3000);
                    // ensure this game is over
                    if (await IsGameOver(session, stopwatch))
                        break;
                    dayNumber++;
                }

                await dbContextAccessor.DbContext.SaveChangesAsync();
            }
            catch (Exception e)
            {
                Log.Error(e, "Error occured in game process");
            }
            finally
            {
                // removing doctor self healing registry
                _healedHimselfRegistry.Remove(sessionId);
            }
        }

        private async Task<bool> ApproveLynch(GameSession session, GameSessionMember gamerForLynch)
        {
            var allowedMembers = session.GetAliveMembers().ToList();
            allowedMembers.Remove(gamerForLynch);
            var approveVotes =
                await _frontend.CreateLynchApprovalVote(session.Room, allowedMembers.ToArray(), gamerForLynch);
            // no votes, skip
            if (!approveVotes.Any())
                return false;

            var groupedVotes = (from vote in approveVotes
                group vote by vote.Approve
                into voteGroup
                select new {key = voteGroup.Key, voices = voteGroup.Count()}).ToList();
            var topVotes = (from voteItem in groupedVotes
                where voteItem.voices == groupedVotes.Max(g => g.voices)
                select voteItem.key).ToList();
            // there are two top result, skip this Lynch... otherwise return it.
            return topVotes.Count == 1 && topVotes[0];
        }

        private async Task<GameSessionMember?> PublicLynchVote(GameSession session)
        {
            var votes = await _frontend.CreateLynchVoteAndReceiveResults(session.Room, session.GetAliveMembers());
            // empty results
            if (!votes.Any())
                return null;

            var groupedVotes = (from vote in votes
                group vote by vote.VoiceTarget.Id
                into voteGroup
                select new {key = voteGroup.Key, elements = voteGroup, voices = voteGroup.Count()}).ToList();
            var topVotes = (from voteItem in groupedVotes
                where voteItem.voices == groupedVotes.Max(g => g.voices)
                select voteItem.elements.First().VoiceTarget).ToList();
            // there are two top result, skip this Lynch... otherwise return it.
            return topVotes.Count > 1 ? null : topVotes[0];
        }

        private async Task<bool> IsGameOver(GameSession session, Stopwatch stopwatch)
        {
            var aliveMembers = session.GetAliveMembers();
            var citizensCount = aliveMembers.Count(g => g.Role != GameRoles.Mafia);
            var mafiaCount = aliveMembers.Length - citizensCount;
            var isGameOver = false;
            var mafiaWins = false;
            // case 1: mafia wins
            if (mafiaCount >= citizensCount)
            {
                isGameOver = true;
                mafiaWins = true;
                foreach (var gameSessionMember in aliveMembers.Where(g => g.Role == GameRoles.Mafia))
                {
                    gameSessionMember.IsWin = true;
                }
            }
            // case 2: citizens wins
            else if (mafiaCount == 0)
            {
                isGameOver = true;
                foreach (var gameSessionMember in aliveMembers.Where(g => g.Role != GameRoles.Mafia))
                {
                    gameSessionMember.IsWin = true;
                }
            }

            // game is not over
            if (!isGameOver) return false;

            stopwatch.Stop();
            var gameOverString =
                new StringBuilder(
                    $"<b>Игра окончена!</b> 🏁\n\n");
            gameOverString.AppendLine($"<b>Победили</b>: {(mafiaWins ? "мафиози 😈" : "мирные жители 👤")}.");
            gameOverString.AppendLine(
                $"<b>Игра длилась</b>: {(int) Math.Round(stopwatch.Elapsed.TotalMinutes)} минут.\n");
            gameOverString.AppendLine("<b>Игроки:</b>");
            gameOverString.Append(session.GetMembersInfo(true, true));
            gameOverString.AppendLine("------");
            gameOverString.AppendLine("Благодарю всех участников игры! :)");
            await _frontend.SendMessageToRoom(session.Room, gameOverString.ToString(), true);

            session.State = GameSessionStates.GameOver;

            return true;
        }

        private async Task InspectGamer(GameSession session, GameRoles inspectorRole, GameSessionMember inspectorTarget)
        {
            await _frontend.SendMessageToGamer(inspectorTarget.GamerAccount, "Кто-то наводит справки по тебе...");
            var roleName = inspectorTarget.Role switch
            {
                GameRoles.Citizen => "мирный житель",
                GameRoles.Cop => "коммисар",
                GameRoles.Doctor => "доктор",
                GameRoles.Mafia => "мафия"
            };
            var messageText =
                $"Наши люди нашли важную информацию: <b>{inspectorTarget.GamerAccount.NickName}</b> это <b>{roleName}</b>.";
            switch (inspectorRole)
            {
                case GameRoles.Cop:
                {
                    var cop = session.GetCop();
                    if (cop == null)
                    {
                        Console.WriteLine($"{session.Id} - Cop is null!");
                        return;
                    }

                    await _frontend.SendMessageToGamer(cop.GamerAccount, messageText);
                    break;
                }
                case GameRoles.Mafia:
                    var mafiaMembers = session.GetMafia();
                    var messageTasks =
                        mafiaMembers.Select(m => _frontend.SendMessageToGamer(m.GamerAccount, messageText));
                    await Task.WhenAll(messageTasks);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(inspectorRole), inspectorRole,
                        "Supported only for Cop and Mafia");
            }
        }

        private async Task<bool> KillGamer(GameSession session, GameRoles killerRole, GameSessionMember actionTarget,
            GameSessionMember? healingTarget)
        {
            // healed by doctor
            if (actionTarget.Id == healingTarget?.Id)
                return false;
            actionTarget.IsDead = true;
            await _frontend.SendMessageToRoom(session.Room,
                $"Был убит: <i>{actionTarget.Role.GetRoleName()}</i> <b>{actionTarget.GamerAccount.NickName}</b>");

            AskForLastWord(session, actionTarget).ConfigureAwait(false);
            return true;
        }

        private async Task AskForLastWord(GameSession session, GameSessionMember actionTarget)
        {
            await _frontend.SendMessageToGamer(actionTarget.GamerAccount,
                "Тебя убили. Ты можешь отправить мне свои последние слова...");
            var lastWords = await _frontend.GetLastWords(actionTarget.GamerAccount);
            // nothing to say, just skip it
            if (lastWords == null)
                return;
            try
            {
                await _frontend.SendMessageToRoom(session.Room,
                    $"Свидетели смерти <b>{actionTarget.GamerAccount.NickName}</b> слышали как он кричал: \n <b><i>{lastWords}</i></b>");
            }
            catch (Exception e)
            {
                Log.Error(e, "Error when tried process last words");
            }
        }

        private Task HealGamer(GameSessionMember healingTarget) =>
            _frontend.SendMessageToGamer(healingTarget.GamerAccount, "Доктор пришел подлечить тебя :)");

        private async Task<ActionDescriptor> AskCopForAction(GameSession session)
        {
            var cop = session.GetCop();
            // there is no cop (return nothing)
            if (cop == null)
                return new ActionDescriptor();
            var aliveMembers = session.GetAliveMembers();
            var selectedAction = await _frontend.AskCopForAction(cop, aliveMembers);
            await _frontend.SendMessageToRoom(session.Room,
                selectedAction.Target != null
                    ? selectedAction.Action == GameActions.Checkup
                        ? "Коммисар поехал в офис, чтобы навести справки!"
                        : "Коммисар зарядил свою пушку..."
                    : "Коммисар не хочет выходить на службу :(");
            return selectedAction;
        }

        private async Task<ActionDescriptor> AskDoctorForAction(GameSession session)
        {
            var doctor = session.GetDoctor();
            // there is no doctor (return nothing)
            if (doctor == null)
                return new ActionDescriptor();
            var selfHealingRegistry = _healedHimselfRegistry[session.Id];
            var alreadyHealedHimself = selfHealingRegistry.Contains(doctor.Id);
            var aliveMembers = session.GetAliveMembers();
            if (alreadyHealedHimself)
            {
                var allMembers = aliveMembers.ToList();
                allMembers.Remove(doctor);
                aliveMembers = allMembers.ToArray();
            }

            var selectedAction = await _frontend.AskDoctorForAction(doctor, aliveMembers);
            await _frontend.SendMessageToRoom(session.Room,
                selectedAction.Target != null
                    ? "Доктор вышел на ночное дежурство!"
                    : "Доктор не хочет выходить на службу :(");

            // doctor heals himself, we allow it only one time.
            // we are going to log this action
            if (selectedAction.Target?.Id == doctor.Id)
            {
                selfHealingRegistry.Add(doctor.Id);
            }

            return selectedAction;
        }

        private async Task<ActionDescriptor> AskMafiaForAction(GameSession session)
        {
            var mafia = session.GetMafia();
            var availableGamers = session.GetAliveMembers();
            var actionTasks = mafia
                .Select(m => _frontend.AskMafiaForAction(m, availableGamers));
            var allActions = await Task.WhenAll(actionTasks);

            // sending pre-result to mafia
            if (allActions.Length > 1)
            {
                var infoBuilder = new StringBuilder("<b>Выбор мафии</b> \n\n");
                foreach (var actionDescriptor in allActions)
                    infoBuilder.AppendLine(
                        $"<b>{actionDescriptor.ActionFrom.GamerAccount.NickName}</b>: {actionDescriptor.Action switch {null => "💤", GameActions.Checkup => "🔎", GameActions.Killing => "🗡"}} {actionDescriptor.Target?.GamerAccount.NickName ?? ""}\n");
                var text = infoBuilder.ToString();
                var preResultMessages = mafia.Select(m => _frontend.SendMessageToGamer(m.GamerAccount, text));
                await Task.WhenAll(preResultMessages);
            }

            // trying to find top action
            var groupedActions = (from action in allActions
                group action by new {target = action.Target, action = action.Action}
                into grouped
                select new {actionInfo = grouped.Key, votes = grouped.Count()}).ToList();
            var topAction = new ActionDescriptor();
            if (groupedActions.Any())
            {
                var maxVotes = groupedActions.Max(a => a.votes);
                var allActionsWithTopResults = groupedActions.Where(a => a.votes == maxVotes)
                    .Select(a =>
                        new ActionDescriptor {Action = a.actionInfo.action, Target = a.actionInfo.target}).ToList();
                topAction = allActionsWithTopResults.Count == 1
                    ? allActionsWithTopResults.First()
                    : allActionsWithTopResults
                        .Where(x => x.Target != null && x.Action != null)
                        .Random();
                if (topAction.Target != null && topAction.Action != null && mafia.Length > 1)
                {
                    var actionText = topAction.Action switch
                    {
                        GameActions.Killing => "убийство",
                        GameActions.Checkup => "проверка",
                        _ => ""
                    };
                    var notifyTopActonTasks = mafia
                        .Select(m => _frontend.SendMessageToGamer(m.GamerAccount,
                            $"Самым популярным решением стало: <b>{actionText} {topAction.Target.GamerAccount.NickName}</b>"));
                    await Task.WhenAll(notifyTopActonTasks);
                }
            }


            await _frontend.SendMessageToRoom(session.Room,
                topAction.Target != null
                    ? topAction.Action == GameActions.Checkup
                        ? "Мафия решила осмотреться!"
                        : "Мафия вышла на охоту!"
                    : "Мафия спит :(");
            return topAction;
        }

        private async Task SendIntroduceMessages(List<GameSessionMember> gamers)
        {
            var introListTasks = (from gameSessionMember in gamers
                let roleText = gameSessionMember.Role switch
                {
                    GameRoles.Citizen => @"Ты мирный житель. 
Твоя задача линчевать мерзавцев на городском собрании.",
                    GameRoles.Cop => @"Ты шериф. 
Твоя задача вычислить мафию до того, как она зальет весь город кровью мирных жителей!",
                    GameRoles.Doctor => "Ты доктор. Твоя задача спасать жителей от нападков мерзкой мафии.",
                    GameRoles.Mafia => "Ты мафия. Твоя задача показать всем кто тут настоящий злодей.",
                    _ => throw new ArgumentOutOfRangeException()
                }
                select _frontend.SendMessageToGamer(gameSessionMember.GamerAccount, $"{roleText} Удачи!")).ToList();

            await Task.WhenAll(introListTasks);
        }

        private void ResolveRoles(GameSession session)
        {
            var players = session.GameMembers.ToList();
            var playersCount = players.Count;

            var enemyCount = (int) Math.Truncate(playersCount / (double) _minimalGamerCount);

            GameRoles CalculateRole(int index)
            {
                if (index < enemyCount)
                    return GameRoles.Mafia;
                else if (index == enemyCount)
                    return GameRoles.Doctor;
                // we are creating cop position, only if count of gamers greater than 4
                else if (playersCount > 4 && index == enemyCount + 1)
                    return GameRoles.Cop;
                else
                    return GameRoles.Citizen;
            }

            var roles = Enumerable.Range(0, players.Count)
                .Select(CalculateRole)
                .ToList();

            var r = new Random();

            var reorderedPlayers = players.Select(x => new
                {
                    Index = r.Next(),
                    Item = x
                })
                .OrderBy(x => x.Index)
                .ToList();

            for (var i = 0; i < players.Count; i++)
                reorderedPlayers[i].Item.Role = roles[i];
        }
    }
}