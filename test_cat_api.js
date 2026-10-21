async function test() {
  const loginRes = await fetch('http://localhost:5025/api/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ mobileNumber: '9999999999', password: '123456' })
  });
  const auth = await loginRes.json();
  const token = auth.accessToken;

  const catRes = await fetch('http://localhost:5025/api/categories?type=ALL', {
    headers: { 'Authorization': 'Bearer ' + token }
  });
  console.log('API Status:', catRes.status);
  const cats = await catRes.json();
  console.log('Total categories from API:', cats.length);
  cats.forEach(c => {
    const subs = (c.subcategories || []).map(s => s.name).join(', ');
    console.log(`- [${c.type.toUpperCase()}] ${c.name} (Id: ${c.id}): ${subs || '(none)'}`);
  });
}
test().catch(console.error);
