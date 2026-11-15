# BuzzDesk privacy policy

*Last updated: November 16, 2026*

BuzzDesk is a task, reminder, topic and link organizer for Windows. It works entirely on your PC.

**What BuzzDesk does not do**

- It does not collect, send or sell personal data.
- It has no accounts, analytics, telemetry, ads or tracking.
- Your tasks, reminders, topics, notes and links never leave your PC unless you share them yourself.

**What stays on your PC**

- Your data, in a SQLite database at `%LOCALAPPDATA%\BuzzDesk\buzzdesk.db`, and its automatic daily backups in `%LOCALAPPDATA%\BuzzDesk\backups`.
- Settings and appearance, in the same folder.

**Network use**

- When you save a link, BuzzDesk asks that link's own site for its icon (favicon). Nothing else is sent; you can turn this off in the link's options.
- The version downloaded from GitHub connects to the internet only when you agree to download a missing component: Microsoft Edge WebView2, from Microsoft.
- Cloud backup is off until you choose a folder. Then BuzzDesk writes copies only into that folder; your cloud client (OneDrive, Google Drive, Dropbox…) uploads them under its own privacy policy. By default the copies are encrypted with your passphrase (AES-256-GCM), so the service can't read them; the passphrase is stored on your PC encrypted with your Windows user (DPAPI) and never sent anywhere.
- AI is off by default. If you turn it on and use an AI feature, only what that feature shows you is sent, to the provider you chose (or to your own PC with Ollama). API keys are stored encrypted with your Windows user (DPAPI).
- Boberto, the animated companion, is rendered by Microsoft Edge WebView2, a Windows component governed by Microsoft's privacy statement.

**Children**

BuzzDesk does not knowingly collect any information from anyone, including children.

**Contact**

Questions about this policy: open an issue at https://github.com/ChristianBuzzetti/BuzzDesk/issues

---

# Política de privacidad de BuzzDesk

*Última actualización: 16 de noviembre de 2026*

BuzzDesk es un organizador de pendientes, recordatorios, temas y enlaces para Windows. Funciona completamente en tu PC.

- No recopila, envía ni vende datos personales. No tiene cuentas, analíticas, telemetría, publicidad ni seguimiento.
- Tus datos quedan en una base SQLite en `%LOCALAPPDATA%\BuzzDesk\buzzdesk.db`, con copias de seguridad diarias en `%LOCALAPPDATA%\BuzzDesk\backups`.
- Al guardar un enlace, BuzzDesk le pide el ícono (favicon) al propio sitio del enlace. Nada más se envía.
- La versión de GitHub solo se conecta si aceptás descargar un componente faltante (WebView2 desde Microsoft).
- La copia en la nube está apagada hasta que elegís una carpeta; entonces BuzzDesk solo escribe copias ahí y tu cliente de nube las sube. Por defecto van cifradas con tu frase (AES-256-GCM): el servicio no las puede leer. La frase queda en tu PC, cifrada con tu usuario de Windows.
- La IA viene apagada. Si la activás y usás una función, solo se envía lo que esa función te muestra, al proveedor que elegiste. Las API keys se guardan cifradas con tu usuario de Windows.
- Consultas: https://github.com/ChristianBuzzetti/BuzzDesk/issues
