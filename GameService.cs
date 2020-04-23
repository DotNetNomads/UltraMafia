using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using UltraMafia.DAL;
using UltraMafia.DAL.Enums;
using UltraMafia.DAL.Model;
using UltraMafia.Frontend;
using UltraMafia.GameModel;

namespace UltraMafia
{
    public class GameService
    {
        private readonly IFrontend _frontend;
        private readonly GameDbContext _dataContext;
        private readonly GameSettings _gameSettings;
        private int _minimalGamerCount;

        public GameService(IFrontend frontend, GameDbContext dataContext, GameSettings gameSettings)
        {
            _frontend = frontend;
            _dataContext = dataContext;
            _gameSettings = gameSettings;
            _minimalGamerCount = _gameSettings.MinGamerCount < 4 ? 4 : _gameSettings.MinGamerCount;
        }

        public void ListenToEvents()
        {
            _frontend.ActionHandler = ActionHandler;
            _frontend.MessageHandler = MessageHandler;
            _frontend.RegistrationHandler = RegistrationHandler;
            _frontend.GameCreationHandler = GameCreationHandler;
            _frontend.GameStartHandler = GameStartHandler;
            _frontend.StopGameHandler = StopGameHandler;
            _frontend.EnableGame();
        }

        private async Task<GameSession> StopGameHandler(GameRoom room, GamerAccount callerAccount)
        {
            var session =
                await _dataContext.GameSessions.FirstOrDefaultAsync(s =>
                    s.RoomId == room.Id && s.State != GameSessionStates.GameOver);
            if (session == null)
            {
                throw new Exception("Игры нет, сначала создай ее, а потом закрывай)");
            }

            if (session.State == GameSessionStates.Playing)
            {
                throw new Exception("Нельзя так! Народ играет!");
            }

            if (session.CreatedByGamerAccountId != callerAccount.Id)
            {
                throw new Exception("Игру может удалить только ее создатель!");
            }

            _dataContext.Remove(session);
            await _dataContext.SaveChangesAsync();
            return session;
        }

        private async Task<GameSession> GameStartHandler(GameRoom room)
        {
            var session =
                await _dataContext.GameSessions
                    .Include(s => s.GameMembers)
                    .Include("GameMembers.GamerAccount")
                    .FirstOrDefaultAsync(s =>
                        s.RoomId == room.Id && s.State != GameSessionStates.GameOver);
            if (session == null)
                throw new Exception("Нет игры которую можно начать. Сначала создайте её!");
            if (session.State == GameSessionStates.Playing)
                throw new Exception("Игра уже играется) Играй чёрт тебя побери)");
            if (session.GameMembers.Count < _minimalGamerCount)
                throw new Exception($"Не хватает игроков, минимальное количество для старта: {_minimalGamerCount}");
            ResolveRoles(session);
            await _frontend.SendMessageToRoom(session.Room,
                $"Игра начинается! Количество игроков мафиози: {session.GameMembers.Count(m => m.Role == GameRoles.Mafia)}");
            session.State = GameSessionStates.Playing;
            await _dataContext.SaveChangesAsync();
            RunGame(session);
            return session;
        }

        private void RunGame(GameSession session)
        {
            Task.Run(async () =>
            {
                try
                {
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
                        await Task.Delay(5000);
                        var doctorActionTask = AskDoctorForAction(session).ConfigureAwait(false);
                        var mafiaActionTask = AskMafiaForAction(session).ConfigureAwait(false);
                        var copActionTask = AskCopForAction(session).ConfigureAwait(false);

                        // resolving actions
                        var doctorAction = await doctorActionTask;
                        GameSessionMember healingTarget = null;
                        if (doctorAction.Action != null)
                        {
                            healingTarget = doctorAction.Target;
                            await HealGamer(healingTarget);
                        }

                        await Task.Delay(2000);

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

                        await Task.Delay(2000);
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

                        await Task.Delay(2000);

                        // ensure this game is over
                        if (await IsGameOver(session, stopwatch))
                            break;

                        await Task.Delay(2000);

                        #region Day logic

                        await _frontend.SendMessageToRoom(session.Room,
                            $@"
День #{dayNumber} ☀️
Все проснулись под птение птичек. 
Пришло время наказать мафию.

<b>Игроки</b>: 
{session.GetMembersInfo(false, true)}

А теперь давайте обсудим прошедшую ночь, затем будем голосовать. 
Время на обсуждение: 2 минуты.
", true);

                        await Task.Delay(120000);
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

                        await Task.Delay(2000);
                        // ensure this game is over
                        if (await IsGameOver(session, stopwatch))
                            break;
                        dayNumber++;
                        await Task.Delay(2000);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }
            });
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
            await _dataContext.SaveChangesAsync();

            return true;
        }

        private async Task InspectGamer(GameSession session, GameRoles instectorRole, GameSessionMember inspectorTarget)
        {
            await _frontend.SendMessageToGamer(inspectorTarget.GamerAccount, "Кто-то наводит справки по тебе...");
            var roleName = inspectorTarget.Role switch
            {
                GameRoles.Citizen => "горожанин",
                GameRoles.Cop => "коммисар",
                GameRoles.Doctor => "доктор",
                GameRoles.Mafia => "мафия"
            };
            var messageText =
                $"Наши люди нашли важную информацию: <b>{inspectorTarget.GamerAccount.NickName}</b> это <b>{roleName}</b>.";
            switch (instectorRole)
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
                    throw new ArgumentOutOfRangeException(nameof(instectorRole), instectorRole,
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
                Console.WriteLine(e);
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
            var aliveMembers = session.GetAliveMembers();
            var selectedAction = await _frontend.AskDoctorForAction(doctor, aliveMembers);
            await _frontend.SendMessageToRoom(session.Room,
                selectedAction.Target != null
                    ? "Доктор вышел на ночное дежурство!"
                    : "Доктор не хочет выходить на службу :(");
            return selectedAction;
        }

        private async Task<ActionDescriptor> AskMafiaForAction(GameSession session)
        {
            var mafia = session.GetMafia();
            var availableGamers = session.GetAliveMembers();
            var actionTasks = mafia
                .Select(m => _frontend.AskMafiaForAction(m, availableGamers));
            var allActions = await Task.WhenAll(actionTasks);
            await Task.Delay(5000);
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
            // how many mafia in the game?
            var resolveEnemyCount = (int) Math.Truncate(session.GameMembers.Count / (double) _minimalGamerCount);
            // copy original list
            var gamersToResolve = session.GameMembers.ToList();
            // resolving enemies
            while (resolveEnemyCount > 0)
            {
                var enemy = gamersToResolve.Random();
                enemy.Role = GameRoles.Mafia;
                gamersToResolve.Remove(enemy);
                resolveEnemyCount--;
            }

            // resolving a doctor
            var doctor = gamersToResolve.Random();
            doctor.Role = GameRoles.Doctor;
            gamersToResolve.Remove(doctor);
            // is there cop? we can resolve cop,
            // only if there are at least 3 players without role.
            if (gamersToResolve.Count > 2)
            {
                var cop = gamersToResolve.Random();
                cop.Role = GameRoles.Cop;
                gamersToResolve.Remove(cop);
            }

            // another gamers are citizens :)
            gamersToResolve.ForEach(g => g.Role = GameRoles.Citizen);
        }

        private async Task<GameSession> GameCreationHandler(GameRoom room, GamerAccount createdByAccount)
        {
            // trying to find existing session
            var sessionExists = _dataContext.GameSessions
                .Any(i =>
                    i.RoomId == room.Id &&
                    new[] {GameSessionStates.Playing, GameSessionStates.Registration}.Contains(i.State));
            if (sessionExists)
            {
                throw new Exception("Нельзя создавать игру там где она уже есть :)");
            }

            var session = new GameSession
            {
                RoomId = room.Id,
                Room = room,
                State = GameSessionStates.Registration,
                CreatedByGamerAccountId = createdByAccount.Id,
                CreatedByGamerAccount = createdByAccount
            };
            await _dataContext.AddAsync(session);
            await _dataContext.SaveChangesAsync();
            return session;
        }

        private async Task<GameSession> RegistrationHandler(GameRoom room, GamerAccount account)
        {
            var session = await _dataContext.GameSessions
                .Include(r => r.Room)
                .Include("GameMembers.GamerAccount")
                .FirstOrDefaultAsync(g => g.RoomId == room.Id && g.State != GameSessionStates.GameOver);
            if (session == null)
                throw new Exception("Игры в данной комнате не существует. Необходимо создать ее.");

            if (session.State != GameSessionStates.Registration)
                throw new Exception("Нельзя зарегистрироваться, игра уже идет");

            if (session.GameMembers.Any(gm => gm.GamerAccountId == account.Id))
                throw new Exception("Да ты уже в игре! Жди :)");

            session.GameMembers.Add(new GameSessionMember
            {
                GamerAccountId = account.Id,
                GameSessionId = session.Id
            });
            await _dataContext.SaveChangesAsync();
            await _frontend.SendMessageToRoom(session.Room, $"{account.NickName}, красавчик, в деле!");
            return session;
        }

        private Task MessageHandler(GamerAccount gamer, string message)
        {
            throw new System.NotImplementedException();
        }

        private Task ActionHandler(
            (GameRoom room, GamerAccount gamerFrom, GameActions action, GamerAccount target) actionInfo)
        {
            throw new NullReferenceException();
        }
    }
}