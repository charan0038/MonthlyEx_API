const connString = 'postgresql://neondb_owner:npg_vpyK6MrjC9bs@ep-wispy-darkness-azf1ttkm-pooler.c-3.ap-southeast-1.aws.neon.tech/neondb?sslmode=require';
const neonSqlUrl = 'https://ep-wispy-darkness-azf1ttkm-pooler.c-3.ap-southeast-1.aws.neon.tech/sql';

async function main() {
  console.log('1. Dropping obsolete mock column "Categories"."Subcategories"...');
  const res = await fetch(neonSqlUrl, {
    method: 'POST',
    headers: { 'Neon-Connection-String': connString, 'Content-Type': 'application/json' },
    body: JSON.stringify({ query: 'ALTER TABLE "Categories" DROP COLUMN IF EXISTS "Subcategories";' })
  });
  console.log('Result status:', res.status);
  const text = await res.text();
  console.log('Response:', text);

  console.log('\n2. Verifying Categories columns:');
  const colRes = await fetch(neonSqlUrl, {
    method: 'POST',
    headers: { 'Neon-Connection-String': connString, 'Content-Type': 'application/json' },
    body: JSON.stringify({ query: "SELECT column_name FROM information_schema.columns WHERE table_name = 'Categories';" })
  });
  const cols = await colRes.json();
  console.log(cols.rows.map(r => r.column_name));

  console.log('\n3. Verifying Subcategories table has all categories and subcategories:');
  const countRes = await fetch(neonSqlUrl, {
    method: 'POST',
    headers: { 'Neon-Connection-String': connString, 'Content-Type': 'application/json' },
    body: JSON.stringify({ query: 'SELECT count(*) as total FROM "Subcategories" WHERE "IsDeleted" = 0;' })
  });
  const counts = await countRes.json();
  console.log('Total active subcategories:', counts.rows[0].total);
}

main().catch(console.error);
