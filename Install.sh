#!/bin/bash
# =============================================================================
#  CS2 Dedicated Server - Instalación completa
#  Incluye: Metamod + CounterStrikeSharp + CS2Fixes + WeaponPaints + InfiniteMoney
#  Testado en: Ubuntu 24.04 LTS
#  Autor: jalster
# =============================================================================
#
#  ARQUITECTURA DE PLUGINS:
#  ┌─────────────────────────────────────────────────────────────┐
#  │  CS2 Engine                                                 │
#  │    └── Metamod        (carga plugins nativos)              │
#  │          └── CSS      (framework .NET para plugins C#)     │
#  │                ├── CS2Fixes      (desbloquea cvars)        │
#  │                ├── WeaponPaints  (skins via !g <code>)     │
#  │                └── InfiniteMoney (dinero cada 15s)         │
#  └─────────────────────────────────────────────────────────────┘
#
#  USO: chmod +x install_cs2server.sh && ./install_cs2server.sh
# =============================================================================
set -e

CS2DIR="$HOME/cs2server"
STEAMCMD="$HOME/steamcmd"
CSGO="$CS2DIR/game/csgo"
ADDONS="$CSGO/addons"
CSS="$ADDONS/counterstrikesharp"
PLUGINS="$CSS/plugins"
SHARED="$CSS/shared"

echo ""
echo "╔══════════════════════════════════════════════════════════════╗"
echo "║           CS2 Dedicated Server - Instalación limpia          ║"
echo "╚══════════════════════════════════════════════════════════════╝"
echo ""

# =============================================================================
echo "==> [1/10] Dependencias del sistema"
# =============================================================================
sudo dpkg --add-architecture i386
sudo apt update -q
sudo apt install -y wget tar curl lib32gcc-s1 unzip jq screen python3 dotnet-sdk-8.0 docker.io
sudo systemctl enable --now docker

# =============================================================================
echo "==> [2/10] SteamCMD"
# =============================================================================
mkdir -p "$STEAMCMD" "$CS2DIR"
cd "$STEAMCMD"
wget -q https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz
tar -xzf steamcmd_linux.tar.gz
rm steamcmd_linux.tar.gz

# =============================================================================
echo "==> [3/10] Descargando servidor CS2 (AppID 730)"
# NOTA: AppID 730 es el correcto para Linux.
#       AppID 2347770 (dedicated server) solo funciona en Windows.
# =============================================================================
"$STEAMCMD/steamcmd.sh" +force_install_dir "$CS2DIR" +login anonymous +app_update 730 validate +quit

# Fix: Steam busca steamclient.so en ~/.steam/sdk64/ pero SteamCMD lo instala
# en ~/steamcmd/linux64/. Creamos el symlink necesario.
mkdir -p ~/.steam/sdk64
ln -sf "$STEAMCMD/linux64/steamclient.so" ~/.steam/sdk64/steamclient.so

# =============================================================================
echo "==> [4/10] Instalando Metamod (última versión auto-detectada)"
# Metamod es el framework base que permite cargar plugins en el servidor.
# Sin él, ningún otro plugin puede cargarse.
# =============================================================================
mkdir -p "$ADDONS"
cd "$ADDONS"

MM_FILE=$(wget -qO- https://mms.alliedmods.net/mmsdrop/2.0/ \
  | grep -oP 'mmsource-2\.0\.0-git\d+-linux\.tar\.gz' \
  | sort -t- -k4 -V | tail -1)
MM_URL="https://mms.alliedmods.net/mmsdrop/2.0/$MM_FILE"
echo "    → Metamod: $MM_FILE"

wget -q "$MM_URL" -O metamod.tar.gz
tar -xzf metamod.tar.gz
rm metamod.tar.gz

# Fix: el tar de Metamod siempre extrae en addons/addons/ (un nivel de más).
if [ -d "$ADDONS/addons/metamod" ]; then
    mv "$ADDONS/addons/metamod"         "$ADDONS/"
    mv "$ADDONS/addons/metamod.vdf"     "$ADDONS/" 2>/dev/null || true
    mv "$ADDONS/addons/metamod_x64.vdf" "$ADDONS/" 2>/dev/null || true
    rm -rf "$ADDONS/addons"
fi

# Fix: metamod_x64.vdf apunta por defecto a bin/linux64/ pero la ruta
# correcta en SteamRT es bin/linuxsteamrt64/
sed -i 's|addons/metamod/bin/linux64/server|addons/metamod/bin/linuxsteamrt64/server|' \
    "$ADDONS/metamod_x64.vdf"

echo "    → metamod_x64.vdf: $(grep file $ADDONS/metamod_x64.vdf | xargs)"

# =============================================================================
echo "==> [5/10] Instalando CounterStrikeSharp (última versión)"
# CSS es el framework de plugins C# para CS2, cargado como plugin de Metamod.
# Usamos "with-runtime" que incluye el runtime .NET embebido.
# El dotnet-sdk-8.0 instalado antes es solo para compilar InfiniteMoney.
# =============================================================================
cd "$ADDONS"

CSS_URL=$(curl -s https://api.github.com/repos/roflmuffin/CounterStrikeSharp/releases/latest \
  | jq -r '.assets[] | select(.name | test("with-runtime-linux")) | .browser_download_url')
CSS_FILE=$(basename "$CSS_URL" | cut -d'?' -f1)
echo "    → CounterStrikeSharp: $CSS_FILE"

wget -q "$CSS_URL" -O css.zip
unzip -q css.zip
rm css.zip

# Fix: mismo problema de anidado que Metamod
if [ -d "$ADDONS/addons/counterstrikesharp" ]; then
    mv "$ADDONS/addons/counterstrikesharp" "$ADDONS/"
    rm -rf "$ADDONS/addons"
fi

# CSS necesita su propio .vdf con sufijo _x64 para que Metamod lo cargue
cat > "$ADDONS/counterstrikesharp_x64.vdf" << 'EOF'
"Plugin"
{
    "file"  "addons/counterstrikesharp/bin/linuxsteamrt64/counterstrikesharp"
}
EOF

# =============================================================================
echo "==> [6/10] Instalando CS2Fixes (última versión)"
# Plugin Metamod nativo que parchea el servidor en memoria para desbloquear
# cvars bloqueados: mp_buy_anywhere, bot_quota, sv_falldamage_scale, etc.
# Sin CS2Fixes estos cvars devuelven "Unknown command" aunque sv_cheats=1.
# =============================================================================
CS2FIXES_URL=$(curl -s https://api.github.com/repos/Source2ZE/CS2Fixes/releases/latest \
  | jq -r '.assets[] | select(.name | test("linux")) | .browser_download_url')
CS2FIXES_FILE=$(basename "$CS2FIXES_URL" | cut -d'?' -f1)
echo "    → CS2Fixes: $CS2FIXES_FILE"

cd "$CSGO"
wget -q "$CS2FIXES_URL" -O cs2fixes.tar.gz
tar -xzf cs2fixes.tar.gz
rm cs2fixes.tar.gz

# =============================================================================
echo "==> [7/10] Instalando WeaponPaints + dependencias + MySQL (Docker)"
# WeaponPaints: aplica skins via !g <gencode> en el chat del servidor.
# Requiere MySQL y tres dependencias CSS:
#   - MenuManagerCS2  → menús interactivos
#   - AnyBaseLibCS2   → librería base compartida
#   - PlayerSettings  → preferencias por jugador
# MySQL en Docker: aislado, sin contaminar el OS, fácil de mantener.
# =============================================================================

# --- MySQL en Docker ---
sudo docker run -d \
  --name cs2mysql \
  --restart unless-stopped \
  -e MYSQL_ROOT_PASSWORD=cs2root \
  -e MYSQL_DATABASE=cs2 \
  -e MYSQL_USER=cs2 \
  -e MYSQL_PASSWORD=cs2pass \
  -p 127.0.0.1:3306:3306 \
  mysql:8.0

echo "    → Esperando a que MySQL inicialice (15s)..."
sleep 15

# --- WeaponPaints ---
WP_URL=$(curl -s https://api.github.com/repos/Nereziel/cs2-WeaponPaints/releases/latest \
  | jq -r '.assets[] | select(.name == "WeaponPaints.zip") | .browser_download_url')
echo "    → WeaponPaints: $WP_URL"

cd "$CSGO"
wget -q "$WP_URL" -O weaponpaints.zip
unzip -q weaponpaints.zip
rm weaponpaints.zip

# Fix: WeaponPaints se extrae en $CSGO/WeaponPaints/ en vez de en plugins/
if [ -d "$CSGO/WeaponPaints" ]; then
    mv "$CSGO/WeaponPaints" "$PLUGINS/WeaponPaints"
fi

# Fix CRÍTICO: weaponpaints.json DEBE estar en counterstrikesharp/gamedata/
# Si no está, el plugin falla con TypeInitializationException (Variables.cs:94)
# Error: "Method CAttributeList_SetOrAddAttributeValueByName not found in gamedata.json"
cp "$PLUGINS/WeaponPaints/gamedata/weaponpaints.json" \
   "$CSS/gamedata/weaponpaints.json"

# --- Dependencias de WeaponPaints ---
# Cada zip extrae en addons/counterstrikesharp/{plugins,shared}/
# Hay que mover el contenido al nivel correcto
mkdir -p /tmp/wp_deps

MM2_URL=$(curl -s https://api.github.com/repos/NickFox007/MenuManagerCS2/releases/latest \
  | jq -r '.assets[0].browser_download_url')
wget -q "$MM2_URL" -O /tmp/wp_deps/menumgr.zip
unzip -q /tmp/wp_deps/menumgr.zip -d /tmp/wp_deps/menumgr

ABL_URL=$(curl -s https://api.github.com/repos/NickFox007/AnyBaseLibCS2/releases/latest \
  | jq -r '.assets[0].browser_download_url')
wget -q "$ABL_URL" -O /tmp/wp_deps/anybaselib.zip
unzip -q /tmp/wp_deps/anybaselib.zip -d /tmp/wp_deps/anybaselib

PS_URL=$(curl -s https://api.github.com/repos/NickFox007/PlayerSettingsCS2/releases/latest \
  | jq -r '.assets[0].browser_download_url')
wget -q "$PS_URL" -O /tmp/wp_deps/playersettings.zip
unzip -q /tmp/wp_deps/playersettings.zip -d /tmp/wp_deps/playersettings

for DEP_DIR in /tmp/wp_deps/menumgr /tmp/wp_deps/anybaselib /tmp/wp_deps/playersettings; do
    if [ -d "$DEP_DIR/addons/counterstrikesharp/plugins" ]; then
        cp -r "$DEP_DIR/addons/counterstrikesharp/plugins/." "$PLUGINS/"
    fi
    if [ -d "$DEP_DIR/addons/counterstrikesharp/shared" ]; then
        cp -r "$DEP_DIR/addons/counterstrikesharp/shared/." "$SHARED/"
    fi
done

rm -rf /tmp/wp_deps

# Config de WeaponPaints con credenciales MySQL pre-configuradas
# Si el servidor arranca sin este archivo, WeaponPaints genera uno vacío
# y hay que rellenarlo manualmente. Pre-creándolo aquí nos lo ahorramos.
mkdir -p "$CSS/configs/plugins/WeaponPaints"
cat > "$CSS/configs/plugins/WeaponPaints/WeaponPaints.json" << 'EOF'
{
  "ConfigVersion": 10,
  "SkinsLanguage": "en",
  "DatabaseHost": "127.0.0.1",
  "DatabasePort": 3306,
  "DatabaseUser": "cs2",
  "DatabasePassword": "cs2pass",
  "DatabaseName": "cs2",
  "CmdRefreshCooldownSeconds": 0,
  "Website": "",
  "Prefix": "[Skins]",
  "Additional": {
    "KnifeEnabled": true,
    "GloveEnabled": true,
    "SkinEnabled": true,
    "AgentEnabled": true,
    "MusicEnabled": true,
    "PinsEnabled": true,
    "CommandWpEnabled": true,
    "CommandKillEnabled": true,
    "CommandKnife": ["knife"],
    "CommandGlove": ["gloves"],
    "CommandAgent": ["agents"],
    "CommandStattrak": ["stattrak", "st"],
    "CommandSkin": ["ws"],
    "CommandSkinSelection": ["skins"],
    "CommandRefresh": ["wp"],
    "GiveRandomKnife": false,
    "GiveRandomSkin": false,
    "ShowSkinImage": true
  },
  "MenuType": "selectable"
}
EOF

# Fix: FollowCS2ServerGuidelines debe ser false para que WeaponPaints pueda
# modificar atributos de armas (skins, knives, gloves). Si es true, CSS
# bloquea estas operaciones por las directrices de Valve.
CORE_JSON="$CSS/configs/core.json"
if [ -f "$CORE_JSON" ]; then
    python3 -c "
import json
with open('$CORE_JSON') as f:
    d = json.load(f)
d['FollowCS2ServerGuidelines'] = False
with open('$CORE_JSON', 'w') as f:
    json.dump(d, f, indent=2)
print('    → core.json: FollowCS2ServerGuidelines = false')
"
else
    mkdir -p "$(dirname $CORE_JSON)"
    cat > "$CORE_JSON" << 'EOF'
{
  "PublicChatTrigger": ["!"],
  "SilentChatTrigger": ["/"],
  "FollowCS2ServerGuidelines": false,
  "PluginHotReloadEnabled": true,
  "PluginAutoLoadEnabled": true,
  "ServerLanguage": "en"
}
EOF
    echo "    → core.json creado con FollowCS2ServerGuidelines = false"
fi

# =============================================================================
echo "==> [8/10] Compilando plugin InfiniteMoney"
# Plugin CSS custom: restaura dinero a 65535 cada 15 segundos.
# No existe cvar nativo en CS2 para dinero infinito mid-round.
# mp_afterroundmoney solo repone al final de ronda, no durante ella.
# =============================================================================
PLUGIN_BUILD="/tmp/InfiniteMoney_build"
mkdir -p "$PLUGIN_BUILD"

cat > "$PLUGIN_BUILD/InfiniteMoney.csproj" << 'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>InfiniteMoney</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CounterStrikeSharp.API" Version="*" />
  </ItemGroup>
</Project>
EOF

cat > "$PLUGIN_BUILD/InfiniteMoney.cs" << 'EOF'
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace InfiniteMoney;

public class InfiniteMoney : BasePlugin
{
    public override string ModuleName => "InfiniteMoney";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "jalster";
    public override string ModuleDescription => "Restores money to 65535 every 15 seconds";

    public override void Load(bool hotReload)
    {
        AddTimer(15.0f, RestoreMoney, CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);
        Logger.LogInformation("InfiniteMoney loaded - restoring money every 15s");
    }

    private void RestoreMoney()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (player.IsValid && !player.IsBot && player.InGameMoneyServices != null)
            {
                player.InGameMoneyServices.Account = 65535;
                Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
            }
        }
    }
}
EOF

cd "$PLUGIN_BUILD"
dotnet build -c Release -o /tmp/InfiniteMoney_out 2>&1 | tail -3

mkdir -p "$PLUGINS/InfiniteMoney"
cp /tmp/InfiniteMoney_out/InfiniteMoney.dll "$PLUGINS/InfiniteMoney/"
echo "    → InfiniteMoney.dll instalado"
rm -rf "$PLUGIN_BUILD" /tmp/InfiniteMoney_out

# =============================================================================
echo "==> [9/10] Configurando gameinfo.gi y cfgs"
# =============================================================================
GAMEINFO="$CSGO/gameinfo.gi"

# gameinfo.gi define los SearchPaths del motor Source 2.
# Metamod DEBE ser la PRIMERA entrada para cargar su libserver.so
# antes que el libserver.so nativo del juego.
# CS2 usa KeyValues — NO soporta comentarios //. Usamos python3 para seguridad.
python3 << PYEOF
import re
path = "$GAMEINFO"
with open(path, "r") as f:
    content = f.read()

content = re.sub(r'\n.*metamod.*', '', content)

old = 'SearchPaths\n\t\t{'
new = 'SearchPaths\n\t\t{\n\t\t\t\tGame\tcsgo/addons/metamod'

if old in content:
    content = content.replace(old, new, 1)
    with open(path, "w") as f:
        f.write(content)
    print("    → gameinfo.gi: metamod añadido como primera entrada")
else:
    print("    → AVISO: SearchPaths no encontrado en gameinfo.gi, revisar manualmente")
PYEOF

mkdir -p "$CSGO/cfg"

# server.cfg: se ejecuta al arrancar pero sus valores pueden ser sobreescritos
# por gamemode_competitive.cfg. Solo ponemos lo esencial aquí.
cat > "$CSGO/cfg/server.cfg" << 'EOF'
hostname "CS2 Local Server"
sv_cheats 1
EOF

# gamemode_competitive_server.cfg: CS2 carga los cfgs en este orden:
#   server_default.cfg → server.cfg → gamemode_competitive.cfg → gamemode_competitive_server.cfg
# Este se carga el ÚLTIMO — nada lo sobreescribe.
# CS2Fixes es NECESARIO para que la mayoría de estos cvars funcionen.
cat > "$CSGO/cfg/gamemode_competitive_server.cfg" << 'EOF'
sv_cheats 1

// Tiempo: rondas de 60 minutos sin freeze ni warmup
mp_timelimit 0
mp_roundtime 60
mp_roundtime_defuse 60
mp_freezetime 0
mp_warmuptime 0

// Dinero: máximo al inicio y al final de ronda
// InfiniteMoney plugin repone a 65535 cada 15s durante la ronda
mp_startmoney 65535
mp_maxmoney 65535
mp_afterroundmoney 65535

// Compra: en cualquier sitio y sin límite de tiempo
mp_buy_anywhere 1
mp_buytime 9999

// Bots: desactivados
bot_quota 0
mp_autoteambalance 0
mp_limitteams 0

// Partida: sin condición de victoria, servidor activo siempre
sv_hibernate_when_empty 0
mp_ignore_round_win_conditions 1
mp_match_restart_delay 5

// Daño: completamente desactivado (balas, granadas y caída)
mp_damage_scale_ct_body 0
mp_damage_scale_ct_head 0
mp_damage_scale_t_body 0
mp_damage_scale_t_head 0
ff_damage_reduction_bullets 0
ff_damage_reduction_grenade 0
ff_damage_reduction_other 0
sv_falldamage_scale 0

// Sin colisiones entre jugadores, sin utilidades comprables
mp_solid_teammates 0
mp_buy_allow_grenades 0
mp_molotov_usable 0

// Respawn inmediato y munición infinita
mp_respawn_on_death_ct 1
mp_respawn_on_death_t 1
sv_infinite_ammo 1
EOF

# =============================================================================
echo "==> [10/10] Script de arranque"
# LD_LIBRARY_PATH: CSS necesita libtier0.so y otras librerías del engine.
#   game/bin/linuxsteamrt64      → librerías base (libtier0, libengine2...)
#   game/csgo/bin/linuxsteamrt64 → librerías del juego (libserver, libhost...)
#   bin/linuxsteamrt64           → runtime de Steam
# =============================================================================
cat > "$CS2DIR/start.sh" << 'EOF'
#!/bin/bash
cd "$(dirname "$0")"
export LD_LIBRARY_PATH="./game/bin/linuxsteamrt64:./game/csgo/bin/linuxsteamrt64:./bin/linuxsteamrt64:$LD_LIBRARY_PATH"
./game/bin/linuxsteamrt64/cs2 \
  -dedicated \
  -insecure \
  +map de_dust2 \
  +game_type 0 \
  +game_mode 1 \
  +exec server.cfg \
  2>&1 | tee server.log
EOF
chmod +x "$CS2DIR/start.sh"

echo ""
echo "╔══════════════════════════════════════════════════════════════╗"
echo "║                  Instalación completada                      ║"
echo "╠══════════════════════════════════════════════════════════════╣"
echo "║  Arrancar:   screen -S cs2 ~/cs2server/start.sh             ║"
echo "║  Detach:     Ctrl+A, D                                       ║"
echo "║  Reconectar: screen -r cs2                                   ║"
echo "║  Conectar:   connect 127.0.0.1  (consola CS2)               ║"
echo "║  Logs:       tail -f ~/cs2server/server.log                  ║"
echo "╠══════════════════════════════════════════════════════════════╣"
echo "║  Plugins:                                                    ║"
echo "║  · Metamod       → framework base de plugins                ║"
echo "║  · CSS           → framework .NET para plugins C#           ║"
echo "║  · CS2Fixes      → desbloquea cvars del juego               ║"
echo "║  · WeaponPaints  → skins via !g <gencode> en el chat        ║"
echo "║  · InfiniteMoney → repone dinero a 65535 cada 15s           ║"
echo "╠══════════════════════════════════════════════════════════════╣"
echo "║  Para inspeccionar skins:                                    ║"
echo "║  1. Ve a cs2inspects.com                                     ║"
echo "║  2. Busca la skin y copia el código !g                       ║"
echo "║  3. Pégalo en el chat del servidor                           ║"
echo "╚══════════════════════════════════════════════════════════════╝"
echo ""