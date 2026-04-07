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