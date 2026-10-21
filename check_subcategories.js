const connString = 'postgresql://neondb_owner:npg_vpyK6MrjC9bs@ep-wispy-darkness-azf1ttkm-pooler.c-3.ap-southeast-1.aws.neon.tech/neondb?sslmode=require';
const neonSqlUrl = 'https://ep-wispy-darkness-azf1ttkm-pooler.c-3.ap-southeast-1.aws.neon.tech/sql';

async function query(sql) {
  const res = await fetch(neonSqlUrl, {
    method: 'POST',
    headers: {
      'Neon-Connection-String': connString,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({ query: sql })
  });
  const data = await res.json();
  return data;
}

async function main() {
  console.log('--- Categories Table Columns ---');
  const catCols = await query(`SELECT column_name FROM information_schema.columns WHERE table_name = 'Categories';`);
  console.log(catCols.rows.map(r => r.column_name));

  console.log('\n--- Categories Data ---');
  const catData = await query(`SELECT "Id", "Name", "Type", "IsDefault" FROM "Categories" WHERE "IsDeleted" = 0 ORDER BY "Id";`);
  console.log(catData.rows);

  console.log('\n--- Subcategories count per Category in Subcategories table ---');
  const subData = await query(`
    SELECT c."Id" as "CategoryId", c."Name" as "CategoryName", count(s."Id") as "SubcategoryCount"
    FROM "Categories" c
    LEFT JOIN "Subcategories" s ON s."CategoryId" = c."Id" AND s."IsDeleted" = 0
    WHERE c."IsDeleted" = 0
    GROUP BY c."Id", c."Name"
    ORDER BY c."Id";
  `);
  console.log(subData.rows);

  console.log('\n--- All Subcategories in Subcategories Table ---');
  const allSubs = await query(`
    SELECT s."Id", s."Name", s."CategoryId", c."Name" as "CategoryName", s."IsDefault", s."UserId", s."CreatedBy"
    FROM "Subcategories" s
    JOIN "Categories" c ON s."CategoryId" = c."Id"
    WHERE s."IsDeleted" = 0
    ORDER BY s."CategoryId", s."Name";
  `);
  console.log(`Total: ${allSubs.rows.length}`);
  console.table(allSubs.rows);
}

main().catch(console.error);
