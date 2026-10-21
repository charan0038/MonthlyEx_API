const crypto = require('crypto');

function base64Url(str) {
  return Buffer.from(str).toString('base64').replace(/=/g, '').replace(/\+/g, '-').replace(/\//g, '_');
}

function createJwt(userId) {
  const header = { alg: 'HS256', typ: 'JWT' };
  const now = Math.floor(Date.now() / 1000);
  const payload = {
    'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier': String(userId),
    'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name': 'Charan',
    'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/mobilephone': '9944763921',
    iss: 'KharchaApi',
    aud: 'KharchaClient',
    exp: now + 3600,
    nbf: now
  };
  const secret = 'KharchaSecureJwtTokenKey2026_WithHighEntropyAtLeast256BitsLong!';
  const head64 = base64Url(JSON.stringify(header));
  const pay64 = base64Url(JSON.stringify(payload));
  const signature = crypto.createHmac('sha256', secret).update(head64 + '.' + pay64).digest('base64').replace(/=/g, '').replace(/\+/g, '-').replace(/\//g, '_');
  return `${head64}.${pay64}.${signature}`;
  
}

async function run() {
  const token = createJwt(1);
  const res = await fetch('http://localhost:5025/api/transactions?monthYear=2026-10&sortBy=date-desc', {
    headers: { 'Authorization': 'Bearer ' + token }
  });
  console.log('Status code:', res.status);
  const text = await res.text();
  console.log('Response body:\n', text);
}

run().catch(console.error);
