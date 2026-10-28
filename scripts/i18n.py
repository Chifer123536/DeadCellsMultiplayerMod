import re
import struct
import sys
from pathlib import Path

MOD = Path(__file__).resolve().parent.parent
LANG_DIR = MOD / "Resourcefile" / "lang"

CALL = re.compile(
    r'(?:Localize|FormatLocalized|GetText\.Instance\.GetString|CoopText\.T|CoopText\.F|\bT|\bF)\(\s*((?:@?"(?:[^"\\]|\\.)*"\s*\+?\s*)+)'
)
LIT = re.compile(r'"((?:[^"\\]|\\.)*)"')
ESCAPES = {"n": "\n", "t": "\t", "r": "\r", '"': '"', "\\": "\\"}

GAME_KEYS = {"Jouer", "Quitter le jeu", "Rise of The Giant", "The Bad Seed", "Fatal Falls",
             "The Queen and the Sea", "DLCP_NAME_TEXT"}

DYNAMIC_KEYS = [
    "General", "Debug", "As in game", "Host", "Back", "OK", "Cancel", "you", "none",
    "Not all players ready", "No continue save", "No local coop id", "Client coop id not received",
    "Client has no continue save", "Client has no local coop id", "Coop world mismatch",
    "Host coop id not received", "Host has no continue save", "Host has no coop id",
    "No pause", "Pause", "Pause and show choice", "Player",
    "Hello", "Yes", "No", "What?!", "Sit", "Dance", "Mock", "Get lost",
]

RU = {
    "Co-op": "Кооператив",
    "Play together with your Steam friends.": "Играйте вместе с друзьями из Steam.",
    "Create lobby": "Создать лобби",
    "Friends-only Steam lobby. Invite a friend once it is created.": "Лобби Steam только для друзей. Пригласите друга, когда оно создастся.",
    "Join a friend": "Присоединиться к другу",
    "Lobbies of Steam friends who are playing right now.": "Лобби друзей Steam, которые сейчас в игре.",
    "Other ways to connect": "Другие способы",
    "Public lobby, lobby code or local network.": "Публичное лобби, вход по коду, локальная сеть.",
    "No friend has an open lobby right now. The list updates by itself.": "Сейчас ни у кого из друзей нет открытого лобби. Список обновляется сам.",
    "Friends with an open lobby:": "Друзья с открытым лобби:",
    "Join {0}'s lobby": "Войти в лобби игрока {0}",
    "Pick a friend in Steam and choose Join Game.": "Выберите друга в Steam и нажмите «Присоединиться к игре».",
    "Create public lobby": "Создать публичное лобби",
    "Anyone with the lobby code can join. The code is copied to the clipboard.": "Войти сможет любой, у кого есть код. Код копируется в буфер обмена.",
    "Join by lobby code": "Войти по коду лобби",
    "Copy the lobby code first, then choose this.": "Сначала скопируйте код лобби, затем выберите этот пункт.",
    "Local network: host": "Локальная сеть: создать",
    "Players on the same network connect by your IP.": "Игроки из той же сети подключаются по вашему IP.",
    "Local network: join": "Локальная сеть: подключиться",
    "Connect by the host's IP and port.": "Подключение по IP и порту хоста.",
    "Share your IP and port with players on the same network.": "Сообщите IP и порт игрокам из вашей сети.",
    "Enter the host's IP and port.": "Укажите IP и порт хоста.",
    "Name: {0}": "Имя: {0}",
    "Your name in the lobby.": "Ваше имя в лобби.",
    "IP: {0}": "IP: {0}",
    "Change the IP address.": "Изменить IP-адрес.",
    "Port: {0}": "Порт: {0}",
    "Change the port.": "Изменить порт.",
    "Start hosting on this IP and port.": "Открыть лобби на этом IP и порту.",
    "Connect": "Подключиться",
    "Connect to the host.": "Подключиться к хосту.",
    "Waiting until every player is ready.": "Ждём, пока все игроки будут готовы.",
    "Lobby": "Лобби",
    "New run": "Новый забег",
    "Start a new co-op run.": "Начать новый совместный забег.",
    "Continue ({0})": "Продолжить ({0})",
    "Continue the multiplayer save.": "Продолжить мультиплеерное сохранение.",
    "Custom mode": "Пользовательский режим",
    "Normal mode": "Обычный режим",
    "Set up and start a custom mode run.": "Настроить и начать забег в пользовательском режиме.",
    "Invite a friend": "Пригласить друга",
    "Open the Steam invite list.": "Открыть список приглашений Steam.",
    "Copy lobby code": "Скопировать код лобби",
    "Choose the multiplayer save slot.": "Выбрать слот мультиплеерного сохранения.",
    "Choose which DLC are used in this session.": "Выбрать DLC для этой сессии.",
    "Close lobby": "Закрыть лобби",
    "Disconnect everyone and go back.": "Отключить всех и вернуться.",
    "Waiting for a friend. Invite someone from Steam.": "Ждём друга. Пригласите кого-нибудь из Steam.",
    "Waiting for players on the local network.": "Ждём игроков из локальной сети.",
    "Everyone is ready. Start the run!": "Все готовы — можно начинать!",
    "Waiting for: {0}": "Ждём: {0}",
    "Waiting for the host to start the run.": "Ждём, пока хост начнёт забег.",
    "Mark yourself ready when you are set.": "Отметьтесь готовым, когда будете на месте.",
    "Ready: yes": "Готов: да",
    "Ready: no": "Готов: нет",
    "Toggle your ready state.": "Переключить готовность.",
    "Leave lobby": "Выйти из лобби",
    "Disconnect and go back.": "Отключиться и вернуться.",
    "Session DLC": "DLC на сессию",
    "Only DLC that every player owns can be enabled.": "Включить можно только DLC, которые есть у всех игроков.",
    "{0}: {1}": "{0}: {1}",
    "On": "вкл.",
    "Off": "выкл.",
    "Toggle this DLC for the session.": "Включить или выключить это DLC в сессии.",
    "unavailable": "недоступно",
    "Not owned by: {0}": "Нет у: {0}",
    "Can't find lobby": "Лобби не найдено",
    "The lobby is closed or the code is wrong.": "Лобби закрыто или код неверный.",
    "Connecting to the lobby...": "Подключение к лобби...",
    "Connecting to Steam lobby...": "Подключение к лобби Steam...",
    "DLC: {0}": "DLC: {0}",
    "DLC: {0} of {1}": "DLC: {0} из {1}",
    "all": "все",
    "none": "нет",
    "you": "вас",
    "Mode: {0}": "Режим: {0}",
    "Local network: {0}:{1}": "Локальная сеть: {0}:{1}",
    "Lobby code: {0}": "Код лобби: {0}",
    "Click the code to copy it.": "Нажмите на код, чтобы скопировать.",
    "Free seat": "Свободно",
    "Ready": "Готов",
    "Not ready": "Не готов",
    "Host": "Хост",
    "OK": "ОК",
    "Cancel": "Отмена",
    "Back": "Назад",
    "General": "Общие",
    "Mod language": "Язык мода",
    "Language of the co-op menus. \"As in game\" follows the game language.": "Язык меню кооператива. «Как в игре» — тот же, что у игры.",
    "As in game": "Как в игре",
    "Could not create the lobby": "Не удалось создать лобби",
    "Make sure Steam is running and try again.": "Проверьте, что Steam запущен, и попробуйте ещё раз.",
    "Steam did not create the lobby. Try again in a moment.": "Steam не создал лобби. Попробуйте ещё раз чуть позже.",
    "Could not join the lobby": "Не удалось войти в лобби",
    "The lobby may be closed. Ask your friend to create it again.": "Возможно, лобби закрыто. Попросите друга создать его заново.",
    "Co-op version mismatch": "Разные версии кооп-мода",
    "Install the same version of the co-op mod on both computers.": "Установите одну и ту же версию мода на обоих компьютерах.",
    "Host cancelled the co-op run launch.": "Хост отменил запуск забега.",
    "Run failed to synchronize with your friend. Please return to the menu and retry.": "Забег не синхронизировался с другом. Вернитесь в меню и попробуйте снова.",
    "Boss Rush": "Босс-раш",
    "The exit stays sealed until the boss falls.": "Выход закрыт, пока босс жив.",
    "Still waiting for your friend at this exit.": "Ждём друга у этого выхода.",
    "Teammate reached the exit. Following in {0} seconds...": "Напарник дошёл до выхода. Переход через {0} с...",
    "You need a health flask charge to revive.": "Для воскрешения нужен заряд фляги.",
    "Revive": "Поднять",
    "Back at the next passage": "Вернётся в следующем переходе",
    "{0} is out until the next passage!": "{0} выбывает до следующего перехода!",
    "You are back in the fight.": "Вы снова в бою.",
    "Revive failed. Get closer and try again.": "Не вышло поднять. Подойдите ближе и попробуйте снова.",
    "You died. You will be back at the next passage.": "Вы погибли. Вернётесь в следующем переходе.",
    "You are down! A teammate can revive you.": "Вы упали! Напарник может вас поднять.",
    "Revived! Your next fall in this level is final.": "Вас подняли! Следующее падение на этом уровне — окончательное.",
    "Revived!": "Вас подняли!",
    "Boss Rush could not synchronize with your friend. Approach the door again to retry.": "Босс-раш не синхронизировался с другом. Подойдите к двери ещё раз.",
    "Boss sync trace logging": "Трассировка синхронизации боссов",
    "Show perf logs": "Логи производительности",
    "Preview party HUD": "Превью плашек напарников",
    "Down": "Упал",
    "Out": "Выбыл",
    "Spectating {0}": "Наблюдение: {0}",
    "Switch player: , / .": "Сменить игрока: , / .",
    "Controls threshold-based [Perf] hitch and slowdown logging.": "Запись подвисаний и замедлений [Perf] в лог.",
    "Steam invite failed": "Не удалось открыть приглашения",
    "Could not open the Steam invite list. Check console logs.": "Не удалось открыть список приглашений Steam. Проверьте, что оверлей Steam включён.",
    "Steam join failed": "Не удалось подключиться",
    "Could not open the Steam friends overlay. Check console logs.": "Не удалось открыть друзей Steam. Проверьте, что оверлей Steam включён.",
    "Choose a friend playing this mod, then select Join Game": "Выберите друга, который играет с этим модом, и нажмите «Присоединиться к игре»",
    "Not all players ready": "Не все игроки готовы.",
    "No continue save": "Нет сохранения для продолжения.",
    "No local coop id": "Сохранение не привязано к кооперативу.",
    "Client coop id not received": "Ждём данные сохранения от игрока.",
    "Client has no continue save": "У игрока нет сохранения для продолжения.",
    "Client has no local coop id": "Сохранение игрока не привязано к кооперативу.",
    "Coop world mismatch": "Сохранения хоста и игрока от разных забегов.",
    "Host coop id not received": "Ждём данные сохранения от хоста.",
    "Host has no continue save": "У хоста нет сохранения для продолжения.",
    "Host has no coop id": "Сохранение хоста не привязано к кооперативу.",
    "Difficulty: {0}": "Сложность: {0}",
    "Difficulty: waiting for host": "Сложность: ждём хоста",
    "Relaxed": "Лёгкая",
    "Standard": "Стандарт",
    "Hardcore": "Хардкор",
    "For {0} players: mob health x{1}, boss health x{2}, mob damage x{3}.":
        "Для {0} игроков: здоровье мобов ×{1}, боссов ×{2}, урон мобов ×{3}.",
    "Co-op difficulty": "Сложность кооператива",
    "Scales mob health, boss health and mob damage by the number of players. Set by the host.":
        "Здоровье мобов и боссов и урон мобов растут с числом игроков. Выбирает хост.",
    "Mobs damage multiplier": "Множитель урона мобов",
    "Scroll pick": "Выбор свитка",
    "What happens for everyone while a player picks a scroll. Set by the host.":
        "Что происходит у всех, пока игрок выбирает свиток. Выбирает хост.",
    "No pause": "Без паузы",
    "Pause": "Пауза",
    "Pause and show choice": "Пауза и показ выбора",
    "{0} is choosing...": "{0} выбирает...",
    "Player": "Игрок",
    "Emotes": "Эмоции",
    "Hello": "Привет",
    "Yes": "Да",
    "No": "Нет",
    "What?!": "Чего?!",
    "Sit": "Сесть",
    "Dance": "Танец",
    "Mock": "Дразнить",
    "Get lost": "Отвали",
    "Host settings": "Настройки хоста",
    "Achievements": "Достижения",
    "Controls": "Управление",
    "Mob health": "Здоровье мобов",
    "Boss health": "Здоровье боссов",
    "Mob damage": "Урон мобов",
    "Extra multiplier on top of co-op difficulty. Set by the host.": "Дополнительный множитель поверх кооп-сложности. Задаёт хост.",
    "Steam achievements": "Достижения Steam",
    "Unlock Steam achievements while playing with the mod. Off until the game restarts once debug cheats were used.": "Открывать достижения Steam при игре с модом. После включения отладочных читов — выключено до перезапуска игры.",
    "Shared achievements": "Общие достижения",
    "Also unlock the achievements your teammates get. They need the same mod version.": "Получать и достижения, которые открыли напарники. У них должна стоять та же версия мода.",
    "Emote wheel: hold T or L3": "Колесо эмоций: держать T или L3",
    "Press the left stick without tilting it, pick with the right stick or the mouse, then release.": "Нажмите левый стик, не наклоняя его, выберите правым стиком или мышью и отпустите.",
}


def unescape(s):
    out = []
    i = 0
    while i < len(s):
        c = s[i]
        if c == "\\" and i + 1 < len(s):
            out.append(ESCAPES.get(s[i + 1], s[i + 1]))
            i += 2
            continue
        out.append(c)
        i += 1
    return "".join(out)


def escape(s):
    return s.replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n").replace("\t", "\\t")


def source_strings():
    found = {}
    for path in sorted(MOD.rglob("*.cs")):
        rel = path.relative_to(MOD).as_posix()
        if rel.split("/")[0] in ("bin", "obj", "ProtocolReplay.Tests") or "/obj/" in rel:
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        for m in CALL.finditer(text):
            parts = LIT.findall(m.group(1))
            if not parts:
                continue
            s = "".join(unescape(p) for p in parts)
            if not s or s in GAME_KEYS:
                continue
            line = text.count("\n", 0, m.start()) + 1
            found.setdefault(s, f"{rel}:{line}")
    for key in DYNAMIC_KEYS:
        found.setdefault(key, "dynamic")
    return found


def read_po(path):
    header = ""
    entries = {}
    msgid = msgstr = None
    cur = None
    fuzzy = False

    def flush():
        nonlocal header
        if msgid is None:
            return
        if msgid == "":
            header = msgstr or ""
        elif not fuzzy:
            entries[msgid] = msgstr or ""

    for raw in path.read_text(encoding="utf-8-sig").splitlines():
        line = raw.strip()
        if line.startswith("#,") and "fuzzy" in line:
            fuzzy = True
        elif line.startswith("msgid "):
            flush()
            fuzzy = fuzzy and cur is None
            msgid = unescape(line[6:].strip()[1:-1])
            msgstr = None
            cur = "id"
        elif line.startswith("msgstr "):
            msgstr = unescape(line[7:].strip()[1:-1])
            cur = "str"
        elif line.startswith('"') and cur:
            val = unescape(line[1:-1])
            if cur == "id":
                msgid += val
            else:
                msgstr += val
        elif line == "":
            flush()
            msgid = msgstr = None
            cur = None
            fuzzy = False
    flush()
    return header, entries


def write_po(path, header, entries, refs):
    out = ['msgid ""', 'msgstr ""']
    for line in header.splitlines(keepends=True):
        out.append(f'"{escape(line)}"')
    out.append("")
    for key, value in entries:
        ref = refs.get(key)
        if ref:
            out.append(f"#: {ref}")
        out.append(f'msgid "{escape(key)}"')
        out.append(f'msgstr "{escape(value)}"')
        out.append("")
    path.write_text("\n".join(out), encoding="utf-8")


def write_mo(path, header, entries):
    pairs = [("", header)] + [(k, v) for k, v in entries if v]
    pairs.sort(key=lambda kv: kv[0].encode("utf-8"))
    ids = [k.encode("utf-8") for k, _ in pairs]
    strs = [v.encode("utf-8") for _, v in pairs]
    n = len(pairs)
    orig_off = 28
    trans_off = orig_off + n * 8
    data_off = trans_off + n * 8
    blob = bytearray()
    orig_table = []
    for b in ids:
        orig_table.append((len(b), data_off + len(blob)))
        blob += b + b"\0"
    trans_table = []
    for b in strs:
        trans_table.append((len(b), data_off + len(blob)))
        blob += b + b"\0"
    out = bytearray(struct.pack("<7I", 0x950412DE, 0, n, orig_off, trans_off, 0, 0))
    for length, off in orig_table:
        out += struct.pack("<2I", length, off)
    for length, off in trans_table:
        out += struct.pack("<2I", length, off)
    out += blob
    path.write_bytes(bytes(out))


def sync():
    src = source_strings()
    order = sorted(src, key=lambda k: (src[k] == "dynamic", src[k]))

    header, ru = read_po(LANG_DIR / "main.ru.po")
    merged = []
    missing = []
    for key in order:
        value = RU.get(key) or ru.get(key, "")
        if not value:
            missing.append(key)
        merged.append((key, value))
    write_po(LANG_DIR / "main.ru.po", header, merged, src)

    en_header, _ = read_po(LANG_DIR / "main.en.po")
    write_po(LANG_DIR / "main.en.po", en_header, [(k, k) for k in order], src)

    for lang in ("es", "zh"):
        h, entries = read_po(LANG_DIR / f"main.{lang}.po")
        kept = [(k, entries[k]) for k in order if entries.get(k)]
        write_po(LANG_DIR / f"main.{lang}.po", h, kept, src)

    print(f"keys: {len(order)}  ru missing: {len(missing)}")
    for key in missing:
        print(f"  MISSING {src[key]}\t{key!r}")
    return not missing


def compile_all():
    for po in sorted(LANG_DIR.glob("main.*.po")):
        header, entries = read_po(po)
        mo = po.with_suffix(".mo")
        write_mo(mo, header, list(entries.items()))
        print(f"{mo.name}: {len(entries)} entries")


if __name__ == "__main__":
    command = sys.argv[1] if len(sys.argv) > 1 else "sync"
    if command == "sync":
        ok = sync()
        compile_all()
        sys.exit(0 if ok else 1)
    elif command == "compile":
        compile_all()
    else:
        raise SystemExit("usage: i18n.py [sync|compile]")
