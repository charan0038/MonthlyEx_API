const connString = 'postgresql://neondb_owner:npg_vpyK6MrjC9bs@ep-wispy-darkness-azf1ttkm-pooler.c-3.ap-southeast-1.aws.neon.tech/neondb?sslmode=require';
const neonSqlUrl = 'https://ep-wispy-darkness-azf1ttkm-pooler.c-3.ap-southeast-1.aws.neon.tech/sql';

async function main() {
  const res = await fetch(neonSqlUrl, {
    method: 'POST',
    headers: { 'Neon-Connection-String': connString, 'Content-Type': 'application/json' },
    body: JSON.stringify({ query: 'SELECT "Id", "Name", "Subcategories" FROM "Categories";' })
  });
  const data = await res.json();
  console.log('Categories Subcategories column values:', data.rows);
}
main();
