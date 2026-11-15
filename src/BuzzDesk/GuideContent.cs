namespace BuzzDesk;

/// <summary>One block of a guide topic: numbered steps, bullet tips, or keyboard shortcuts.</summary>
public sealed record GuideBlock(string Heading, string Kind, IReadOnlyList<(string a, string b)> Items);

/// <summary>A guide topic. <see cref="Try"/> are (button label, action id) handled by the main window.</summary>
public sealed record GuideTopic(string Id, string Glyph, string Title, string Lead, IReadOnlyList<GuideBlock> Blocks, IReadOnlyList<(string Label, string Action)> Try, bool AiOnly = false);

/// <summary>
/// Everything BuzzDesk can do, written for people (not developers), in both languages. Each topic: what it is,
/// how to use it step by step, tips and shortcuts, and buttons that open the feature right away.
/// </summary>
public static class GuideContent
{
    static string S(string es, string en) => L.Es ? es : en;
    static GuideBlock Steps(string heading, params string[] items) => new(heading, "steps", items.Select(i => (i, "")).ToList());
    static GuideBlock Tips(string heading, params string[] items) => new(heading, "tips", items.Select(i => (i, "")).ToList());
    static GuideBlock Keys(string heading, params (string key, string what)[] items) => new(heading, "keys", items);

    public static IReadOnlyList<GuideTopic> All()
    {
        var st = Store.Settings;
        string quick = st.HotkeyCapture;
        var list = new List<GuideTopic>
        {
            new("start", "", S("Primeros pasos", "Getting started"),
                S("BuzzDesk ordena tu día de trabajo y lo personal: pendientes, recordatorios, temas en seguimiento y enlaces útiles, todo en tu PC. Vive en la bandeja del sistema, así que te avisa aunque cierres la ventana.",
                  "BuzzDesk organizes your work day and personal life: tasks, reminders, topics you follow up and useful links, all on your PC. It lives in the system tray, so it reminds you even when the window is closed."),
                [
                    Steps(S("EN UN MINUTO", "IN ONE MINUTE"),
                        S($"Tocá {quick} desde cualquier programa y escribí lo que tenés que hacer. Enter y listo.", $"Press {quick} from any app and type what you need to do. Enter and done."),
                        S("Abrí Hoy: ahí está lo que vence hoy, lo atrasado, los recordatorios del día y los temas a revisar.", "Open Today: what's due today, what's late, today's reminders and the topics to review."),
                        S("Usá Pendientes para planificar, Temas para seguir cosas que dependen de otros y Enlaces para tus accesos de siempre.", "Use Tasks to plan, Topics to follow things that depend on others and Links for your everyday shortcuts.")),
                    Tips(S("BUENO SABER", "GOOD TO KNOW"),
                        S("Cerrar la ventana no cierra BuzzDesk: sigue en la bandeja, junto al reloj. Para salir del todo, clic derecho en el ícono → Salir.", "Closing the window doesn't quit BuzzDesk: it stays in the tray, next to the clock. To quit, right-click the icon → Exit."),
                        S("Activá Vista → Iniciar con Windows para que los recordatorios funcionen desde que prendés la PC.", "Turn on View → Start with Windows so reminders work from the moment you turn on the PC."),
                        S("Todo se guarda en tu PC. Nada se envía a internet salvo que vos uses una función de IA.", "Everything stays on your PC. Nothing goes online unless you use an AI feature.")),
                ],
                [(S("Ir a Hoy", "Go to Today"), "today"), (S("Captura rápida", "Quick capture"), "quick")]),

            new("today", "", S("Hoy", "Today"),
                S("La pantalla de inicio: lo que tenés que mirar hoy, sin buscar.", "The home screen: what you need to look at today, without searching."),
                [
                    Tips(S("QUÉ MUESTRA", "WHAT IT SHOWS"),
                        S("Atrasados (en rojo) y lo que vence hoy, ordenado por prioridad.", "Overdue (in red) and what's due today, sorted by priority."),
                        S("Los recordatorios del día, con su hora.", "Today's reminders, with their time."),
                        S("Los temas cuya fecha de revisión llegó, y a quién estás esperando.", "Topics whose review date has come, and who you are waiting on."),
                        S("Tus enlaces fijados, a un clic.", "Your pinned links, one click away.")),
                    Tips(S("DESDE ACÁ", "FROM HERE"),
                        S("Marcá un pendiente como hecho con su casilla, o posponelo a mañana con el botón de reloj.", "Tick a task's box to complete it, or postpone it to tomorrow with the clock button."),
                        S("\"Hoy\" usa tu zona horaria: la de Windows, o la que elijas en Vista → Zona horaria.", "\"Today\" uses your time zone: Windows' own, or the one you choose in View → Time zone.")),
                ],
                [(S("Ir a Hoy", "Go to Today"), "today")]),

            new("tasks", "", S("Pendientes", "Tasks"),
                S("Todo lo que tenés que hacer, con prioridad, vencimiento, subtareas, recurrencia y etiquetas.", "Everything you need to do, with priority, due date, subtasks, recurrence and tags."),
                [
                    Steps(S("CREAR Y COMPLETAR", "CREATE AND COMPLETE"),
                        S("Ctrl+N o \"Nuevo pendiente\". Escribí el título, elegí área o proyecto, prioridad y vencimiento.", "Ctrl+N or \"New task\". Type the title, pick the area or project, priority and due date."),
                        S("Agregá subtareas para dividirlo en pasos.", "Add subtasks to split it into steps."),
                        S("Tildalo cuando esté hecho. Si es recurrente, aparece solo el siguiente.", "Tick it when done. If it repeats, the next one appears by itself.")),
                    Keys(S("RECURRENCIA", "RECURRENCE"),
                        (S("Diaria", "Daily"), S("Cada N días.", "Every N days.")),
                        (S("Semanal", "Weekly"), S("Los días de la semana que elijas, cada N semanas.", "The weekdays you choose, every N weeks.")),
                        (S("Mensual", "Monthly"), S("Un día del mes (si el mes es más corto, el último día).", "A day of the month (if the month is shorter, its last day).")),
                        (S("Último día hábil", "Last business day"), S("El último lunes a viernes del mes: cierres, informes, backups.", "The last Monday-to-Friday of the month: closings, reports, backups."))),
                    Tips(S("ORDEN", "ORDER"),
                        S("Áreas (Trabajo, Personal) y proyectos dentro de cada área, en la barra lateral.", "Areas (Work, Personal) and projects inside each area, in the sidebar."),
                        S("Etiquetas libres para cruzar: #urgente, #reunión, #cliente.", "Free tags to cross-cut: #urgent, #meeting, #client."),
                        S("Posponer cuenta cuántas veces lo pateaste: si un pendiente se pospone mucho, quizás haya que partirlo.", "Postponing counts how many times you pushed it: if a task keeps moving, maybe it needs splitting.")),
                ],
                [(S("Ir a Pendientes", "Go to Tasks"), "tasks")]),

            new("reminders", "", S("Recordatorios", "Reminders"),
                S("Avisos de Windows a la hora que elijas, aunque la ventana esté cerrada.", "Windows notifications at the time you choose, even with the window closed."),
                [
                    Steps(S("CÓMO FUNCIONAN", "HOW THEY WORK"),
                        S("Poné un recordatorio en un pendiente, en un tema, o suelto desde la captura rápida.", "Set a reminder on a task, on a topic, or on its own from quick capture."),
                        S("A la hora indicada aparece una notificación de Windows.", "At that time a Windows notification shows up."),
                        S("Desde la notificación: Posponer 10 min, 1 hora o hasta mañana, o marcarlo como hecho.", "From the notification: snooze 10 min, 1 hour or until tomorrow, or mark it done.")),
                    Tips(S("BUENO SABER", "GOOD TO KNOW"),
                        S("BuzzDesk tiene que estar corriendo (en la bandeja alcanza). Activá Iniciar con Windows.", "BuzzDesk must be running (the tray is enough). Turn on Start with Windows."),
                        S("Si la PC estuvo apagada, los recordatorios vencidos aparecen al volver.", "If the PC was off, the missed reminders show up when it's back."),
                        S("Si Windows está en \"No molestar\", los avisos quedan en el Centro de notificaciones.", "If Windows is in Do not disturb, notices wait in the Notification Center.")),
                ],
                []),

            new("topics", "", S("Seguimiento de temas", "Topic follow-up"),
                S("Para lo que no es una tarea sino un asunto que avanza en el tiempo: un incidente, un pedido a otra área, una compra, una migración.", "For what isn't a task but a matter that moves over time: an incident, a request to another team, a purchase, a migration."),
                [
                    Steps(S("CÓMO SE USA", "HOW TO USE IT"),
                        S("Creá un tema con su estado: activo, esperando, en pausa o cerrado.", "Create a topic with its status: active, waiting, paused or closed."),
                        S("Si depende de alguien, marcá \"Esperando a…\" y elegí la persona: queda a la vista desde cuándo esperás.", "If it depends on someone, set \"Waiting on…\" and pick the person: you see since when you've been waiting."),
                        S("Poné la próxima fecha de revisión: ese día aparece en Hoy.", "Set the next review date: that day it shows up in Today."),
                        S("Sumá notas a la línea de tiempo cada vez que pasa algo. Los cambios de estado se anotan solos.", "Add notes to the timeline every time something happens. Status changes are logged by themselves.")),
                    Tips(S("VÍNCULOS", "LINKS"),
                        S("Un tema puede tener pendientes (lo que te toca hacer) y enlaces (el ticket, el documento, el dashboard).", "A topic can have tasks (what you have to do) and links (the ticket, the document, the dashboard).")),
                ],
                [(S("Ir a Temas", "Go to Topics"), "topics")]),

            new("links", "", S("Enlaces útiles", "Useful links"),
                S("Tus accesos de siempre: consolas, dashboards, documentación, sistemas internos.", "Your everyday shortcuts: consoles, dashboards, documentation, internal systems."),
                [
                    Tips(S("QUÉ PODÉS HACER", "WHAT YOU CAN DO"),
                        S("Guardar URL, título, descripción y etiquetas. El ícono del sitio se descarga solo.", "Save URL, title, description and tags. The site icon is fetched by itself."),
                        S("Fijar los más usados: aparecen en Hoy.", "Pin the ones you use most: they show up in Today."),
                        S("Lanzador rápido: escribí parte del nombre y Enter para abrirlo en el navegador.", "Quick launcher: type part of the name and press Enter to open it in the browser."),
                        S("Pegá una URL en la captura rápida para guardarla como enlace.", "Paste a URL in quick capture to save it as a link.")),
                ],
                [(S("Ir a Enlaces", "Go to Links"), "links")]),

            new("quick", "", S("Captura rápida", "Quick capture"),
                S("Una ventanita junto al cursor para anotar algo en dos segundos, sin dejar lo que estás haciendo.", "A small window next to the cursor to jot something down in two seconds, without leaving what you're doing."),
                [
                    Steps(S("CÓMO SE USA", "HOW TO USE IT"),
                        S($"Tocá {quick} desde cualquier programa.", $"Press {quick} from any app."),
                        S("Elegí qué es: pendiente, nota o recordatorio (Tab cambia).", "Choose what it is: task, note or reminder (Tab switches)."),
                        S("Escribí y Enter. Esc cierra sin guardar.", "Type and press Enter. Esc closes without saving.")),
                    Keys(S("ATAJOS DENTRO DEL TEXTO", "SHORTCUTS IN THE TEXT"),
                        ("!1 !2 !3", S("Prioridad baja, media o alta.", "Low, medium or high priority.")),
                        (S("hoy, mañana, lunes…", "today, tomorrow, monday…"), S("Vencimiento o día del recordatorio.", "Due date or reminder day.")),
                        ("15:30", S("Hora del recordatorio.", "Reminder time.")),
                        ("#etiqueta", S("Agrega una etiqueta.", "Adds a tag."))),
                ],
                [(S("Abrir la captura rápida", "Open quick capture"), "quick")]),

            new("search", "", S("Búsqueda", "Search"),
                S("Una sola caja busca en pendientes, temas, notas y enlaces.", "One box searches tasks, topics, notes and links."),
                [
                    Tips(S("CÓMO BUSCAR", "HOW TO SEARCH"),
                        S("Ctrl+F y escribí. No importan mayúsculas ni tildes: \"revision\" encuentra \"revisión\".", "Ctrl+F and type. Case and accents don't matter."),
                        S("Busca por el comienzo de las palabras: \"migra\" encuentra \"migración\".", "It matches word beginnings: \"migra\" finds \"migration\"."),
                        S("Entre comillas, la frase exacta: \"plan de backup\".", "In quotes, the exact phrase: \"backup plan\"."),
                        S("Enter abre el primer resultado.", "Enter opens the first result.")),
                ],
                [(S("Buscar", "Search"), "search")]),

            new("keys", "", S("Atajos de teclado", "Keyboard shortcuts"),
                S("Todos los atajos en un lugar. El global funciona desde cualquier programa.", "All shortcuts in one place. The global one works from any app."),
                [
                    Keys(S("GLOBAL", "GLOBAL"), (quick, S("Captura rápida", "Quick capture"))),
                    Keys(S("EN LA VENTANA", "IN THE WINDOW"),
                        ("Ctrl+1 … Ctrl+4", S("Hoy, Pendientes, Temas, Enlaces", "Today, Tasks, Topics, Links")),
                        ("Ctrl+N", S("Nuevo en la vista actual", "New in the current view")),
                        ("Ctrl+F", S("Buscar en todo", "Search everything")),
                        ("F1", S("Esta guía", "This guide"))),
                ],
                []),

            new("data", "", S("Tus datos y copias de seguridad", "Your data and backups"),
                S("Todo vive en una base SQLite en tu PC, con copia de seguridad automática todos los días.", "Everything lives in a SQLite database on your PC, with an automatic backup every day."),
                [
                    Tips(S("DÓNDE ESTÁ CADA COSA", "WHERE THINGS ARE"),
                        S("Base de datos: %LOCALAPPDATA%\\BuzzDesk\\buzzdesk.db.", "Database: %LOCALAPPDATA%\\BuzzDesk\\buzzdesk.db."),
                        S("Copias: %LOCALAPPDATA%\\BuzzDesk\\backups, una por día (14 días) y una por semana (8 semanas).", "Backups: %LOCALAPPDATA%\\BuzzDesk\\backups, one per day (14 days) and one per week (8 weeks)."),
                        S("Archivo → Hacer una copia de seguridad ahora, cuando quieras.", "File → Back up now, whenever you want.")),
                    Tips(S("PARA RESTAURAR", "TO RESTORE"),
                        S("Salí de BuzzDesk, copiá la copia que quieras sobre buzzdesk.db y volvé a abrir.", "Exit BuzzDesk, copy the backup you want over buzzdesk.db and open it again."),
                        S("Cada copia se verifica (quick_check) al crearse.", "Each backup is verified (quick_check) when created.")),
                ],
                [(S("Abrir la carpeta de datos", "Open the data folder"), "folder")]),

            new("ai", "", S("Inteligencia artificial", "Artificial intelligence"),
                S("Conectá el servicio de IA que prefieras (Claude, Gemini, ChatGPT, OpenRouter, Ollama local u otro) para las funciones de IA que lleguen a BuzzDesk (resúmenes del día y de la semana, en preparación). Viene apagada: nada se envía hasta que la configures y la actives.",
                  "Connect the AI service you prefer (Claude, Gemini, ChatGPT, OpenRouter, local Ollama or another) for the AI features coming to BuzzDesk (daily and weekly summaries, in the works). It comes off: nothing is sent until you set it up and turn it on."),
                [
                    Steps(S("CONECTAR", "CONNECT"),
                        S("Barra lateral → IA (o menú IA → Configurar IA). Agregá una conexión y elegí el proveedor.", "Sidebar → AI (or AI menu → Set up AI). Add a connection and pick the provider."),
                        S("Pegá tu API key (con \"Conseguir una API key\" vas directo a la página del proveedor) y tocá \"Ver modelos\".", "Paste your API key (\"Get an API key\" takes you to the provider's page) and press \"See models\"."),
                        S("Probá la conexión y guardá. Podés tener varias y elegir cuál usar.", "Test the connection and save. You can have several and choose which one to use.")),
                    Tips(S("BUENO SABER", "GOOD TO KNOW"),
                        S("Las keys gratuitas tienen pocos pedidos por minuto: si el servicio pide esperar, verás una cuenta regresiva. Podés cancelar cuando quieras.", "Free keys allow few requests per minute: if the service asks to wait you'll see a countdown. You can cancel any time."),
                        S("Con Ollama la IA corre en tu PC y nada sale de ella.", "With Ollama the AI runs on your PC and nothing leaves it.")),
                ],
                [(S("Configurar IA", "Set up AI"), "ai")]),

            new("personalize", "", S("Personalizar", "Personalize"),
                S("Hacé tuya la app: paletas, color de acento, tipografías, bordes, animaciones, el logo y a Boberto, la mascota que te acompaña.", "Make the app yours: palettes, accent color, fonts, corners, animations, the logo and Boberto, the companion that keeps you company."),
                [
                    Tips(S("QUÉ PODÉS CAMBIAR", "WHAT YOU CAN CHANGE"),
                        S("Estilo: 23 paletas claras y oscuras, y un color de acento propio.", "Style: 23 light and dark palettes, and your own accent color."),
                        S("Texto: tipografías de la interfaz y de los datos, y el tamaño.", "Text: interface and data fonts, and the size."),
                        S("Interfaz: paneles y botones redondeados o rectos, por separado. Y el idioma (español o inglés).", "Interface: rounded or square panels and buttons, separately. And the language (Spanish or English)."),
                        S("Efectos y logo: la forma, los ojos y los colores del logo; animaciones y movimiento.", "Effects & Logo: the logo's shape, eyes and colors; animations and motion."),
                        S("Boberto Studio: gestos, ropa, accesorios, fondos y marcos para Boberto.", "Boberto Studio: gestures, outfits, accessories, backgrounds and frames for Boberto.")),
                ],
                [(S("Abrir Personalizar", "Open Personalize"), "personalize")]),

            new("privacy", "\uE72E", S("Privacidad", "Privacy"),
                S("Tus datos son tuyos: quedan en tu PC.", "Your data is yours: it stays on your PC."),
                [
                    Tips(S("QUÉ SALE DE TU PC", "WHAT LEAVES YOUR PC"),
                        S("Nada, salvo el ícono de cada enlace que guardes (se pide al sitio del enlace) y lo que mandes a la IA si la activás.", "Nothing, except the icon of each link you save (requested from that link's site) and what you send to AI if you turn it on."),
                        S("Las API keys de IA se guardan cifradas con tu usuario de Windows.", "AI API keys are stored encrypted with your Windows user.")),
                ],
                [(S("Abrir la carpeta de datos", "Open the data folder"), "folder")]),
        };
        return list;
    }
}
