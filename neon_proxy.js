const net = require('net');

const NEON_HOST = 'ep-wispy-darkness-azf1ttkm-pooler.c-3.ap-southeast-1.aws.neon.tech';
const WS_URL = `wss://${NEON_HOST}/v2?address=${encodeURIComponent(NEON_HOST + ':5432')}`;
const LOCAL_PORT = 5433;

const server = net.createServer((socket) => {
  const ws = new WebSocket(WS_URL);
  ws.binaryType = 'arraybuffer';
  const pending = [];

  ws.onopen = () => {
    for (const chunk of pending) {
      ws.send(chunk);
    }
    pending.length = 0;
  };

  ws.onmessage = (event) => {
    socket.write(Buffer.from(event.data));
  };

  ws.onerror = (err) => {
    socket.destroy();
  };

  ws.onclose = () => {
    socket.end();
  };

  socket.on('data', (chunk) => {
    if (ws.readyState === 1 /* WebSocket.OPEN */) {
      ws.send(chunk);
    } else {
      pending.push(chunk);
    }
  });

  socket.on('error', (err) => {
    ws.close();
  });

  socket.on('close', () => {
    ws.close();
  });
});

server.listen(LOCAL_PORT, '127.0.0.1', () => {
  console.log(`[NeonProxy] Ready on 127.0.0.1:${LOCAL_PORT} -> ${NEON_HOST}:443 (WSS)`);
});
