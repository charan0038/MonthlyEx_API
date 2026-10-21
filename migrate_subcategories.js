const connString = 'postgresql://neondb_owner:npg_vpyK6MrjC9bs@ep-wispy-darkness-azf1ttkm-pooler.c-3.ap-southeast-1.aws.neon.tech/neondb?sslmode=require';
const neonSqlUrl = 'https://ep-wispy-darkness-azf1ttkm-pooler.c-3.ap-southeast-1.aws.neon.tech/sql';

async function executeSql(query) {
  const clean = query.trim();
  if (!clean) return;

  const res = await fetch(neonSqlUrl, {
    method: 'POST',
    headers: {
      'Neon-Connection-String': connString,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({ query: clean })
  });

  const text = await res.text();
  if (!res.ok) {
    throw new Error(`SQL Error (${res.status}) on [${clean.slice(0, 60)}...]: ${text}`);
  }
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

async function migrate() {
  console.log('--- 1. Creating Subcategories table & adding Transaction columns ---');

  // 1. Create Subcategories Table
  await executeSql(`
    CREATE TABLE IF NOT EXISTS "Subcategories" (
      "Id" SERIAL PRIMARY KEY,
      "Name" VARCHAR(50) NOT NULL,
      "CategoryId" INTEGER NOT NULL REFERENCES "Categories"("Id") ON DELETE CASCADE,
      "IsDefault" BOOLEAN NOT NULL DEFAULT FALSE,
      "CreatedBy" INTEGER NOT NULL DEFAULT 1,
      "CreatedDate" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
      "UpdatedBy" INTEGER NULL,
      "UpdatedDate" TIMESTAMP WITH TIME ZONE NULL,
      "IsDeleted" INTEGER NOT NULL DEFAULT 0
    );
  `);
  console.log('✓ Table "Subcategories" is ready.');

  // 2. Add Columns to Transactions
  await executeSql(`ALTER TABLE "Transactions" ADD COLUMN IF NOT EXISTS "SubcategoryId" INTEGER NULL REFERENCES "Subcategories"("Id") ON DELETE SET NULL;`);
  console.log('✓ Column "Transactions"."SubcategoryId" added or already exists.');

  await executeSql(`ALTER TABLE "Transactions" ADD COLUMN IF NOT EXISTS "Subcategory" VARCHAR(50) NULL;`);
  console.log('✓ Column "Transactions"."Subcategory" added or already exists.');

  // 3. Create Indices
  await executeSql(`CREATE INDEX IF NOT EXISTS "IX_Subcategories_CategoryId_Name_CreatedBy" ON "Subcategories" ("CategoryId", "Name", "CreatedBy");`);
  await executeSql(`CREATE INDEX IF NOT EXISTS "IX_Transactions_SubcategoryId" ON "Transactions" ("SubcategoryId");`);
  console.log('✓ Indices created.');

  // 4. Seed default subcategories
  console.log('--- 2. Checking & Seeding Default Subcategories ---');
  const catRes = await executeSql(`SELECT "Id", "Name" FROM "Categories" WHERE "IsDefault" = TRUE AND "IsDeleted" = 0;`);
  const categories = catRes.rows || [];
  console.log(`Found ${categories.length} default categories in database.`);

  const subcatMap = {
    'Food': ['Groceries', 'Restaurant', 'Coffee', 'Snacks', 'Delivery', 'Fruits & Veggies'],
    'Transport': ['Fuel', 'Car', 'Bus', 'Train', 'Taxi', 'Metro', 'Bike', 'Flight'],
    'Rent': ['Apartment', 'House', 'Office', 'Maintenance'],
    'Bills': ['Electricity', 'Water', 'Internet', 'Mobile', 'Gas'],
    'Shopping': ['Clothes', 'Electronics', 'Footwear', 'Home', 'Gifts'],
    'Health': ['Medicine', 'Doctor', 'Gym', 'Lab Tests', 'Insurance'],
    'Entertainment': ['Movies', 'Streaming', 'Gaming', 'Events', 'Outing'],
    'Other': ['General', 'Miscellaneous'],
    'Salary': ['Monthly Salary', 'Bonus', 'Overtime'],
    'Freelance': ['Project', 'Client', 'Consulting'],
    'Investment': ['Stocks', 'Mutual Funds', 'Crypto', 'Real Estate', 'Dividends']
  };

  let insertedCount = 0;
  for (const cat of categories) {
    const defaultSubs = subcatMap[cat.Name];
    if (!defaultSubs) continue;

    for (const subName of defaultSubs) {
      const check = await executeSql(`SELECT "Id" FROM "Subcategories" WHERE "CategoryId" = ${cat.Id} AND LOWER("Name") = LOWER('${subName.replace(/'/g, "''")}') AND "IsDeleted" = 0;`);
      if (!check.rows || check.rows.length === 0) {
        await executeSql(`
          INSERT INTO "Subcategories" ("Name", "CategoryId", "IsDefault", "CreatedBy", "CreatedDate", "IsDeleted")
          VALUES ('${subName.replace(/'/g, "''")}', ${cat.Id}, TRUE, 1, NOW(), 0);
        `);
        insertedCount++;
      }
    }
  }
  console.log(`✓ Inserted ${insertedCount} default subcategories.`);

  // 5. Verification
  console.log('--- 3. Verifying Subcategories in Database ---');
  const totalCount = await executeSql(`SELECT count(*) as count FROM "Subcategories" WHERE "IsDeleted" = 0;`);
  console.log(`Total active subcategories in Neon DB: ${totalCount.rows[0].count}`);

  const sample = await executeSql(`
    SELECT c."Name" as "Category", s."Name" as "Subcategory", s."IsDefault"
    FROM "Subcategories" s
    JOIN "Categories" c ON s."CategoryId" = c."Id"
    LIMIT 10;
  `);
  console.log('Sample Subcategories:');
  console.table(sample.rows);

  console.log('\nMigration completed successfully!');
}

migrate().catch(err => {
  console.error('Migration failed:', err);
  process.exit(1);
});
