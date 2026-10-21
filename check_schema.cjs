const { Client } = require('pg');

const client = new Client({
  connectionString: 'postgresql://neondb_owner:npg_c9b4eGZomkFq@127.0.0.1:5433/neondb?sslmode=disable'
});

async function main() {
  await client.connect();
  const txCols = await client.query(`
    SELECT column_name, data_type 
    FROM information_schema.columns 
    WHERE table_name = 'Transactions'
    ORDER BY ordinal_position
  `);
  console.log('Transactions columns:', txCols.rows.map(r => `${r.column_name} (${r.data_type})`).join(', '));

  const catCols = await client.query(`
    SELECT column_name, data_type 
    FROM information_schema.columns 
    WHERE table_name = 'Categories'
    ORDER BY ordinal_position
  `);
  console.log('Categories columns:', catCols.rows.map(r => `${r.column_name} (${r.data_type})`).join(', '));

  await client.end();
}

main().catch(err => {
  console.error(err);
  process.exit(1);
});
