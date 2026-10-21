const fs = require('fs');
const path = require('path');

const connString = 'postgresql://neondb_owner:npg_vpyK6MrjC9bs@ep-wispy-darkness-azf1ttkm-pooler.c-3.ap-southeast-1.aws.neon.tech/neondb?sslmode=require';
const neonSqlUrl = 'https://ep-wispy-darkness-azf1ttkm-pooler.c-3.ap-southeast-1.aws.neon.tech/sql';

async function executeSql(query) {
  const clean = query.trim();
  if (!clean || clean.startsWith('--')) return;

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
    throw new Error(`SQL Error (${res.status}) on [${clean.slice(0, 50)}...]: ${text}`);
  }
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

// Split SQL file into statements, preserving DO $$ blocks and functions
function splitStatements(sql) {
  const statements = [];
  let current = '';
  let inDollarQuote = false;
  let dollarTag = '';

  const lines = sql.split('\n');
  for (const line of lines) {
    const trimmed = line.trim();
    if (trimmed.startsWith('--')) continue;

    // Check for $$ or $EF$ dollar quoting
    const dollarMatch = line.match(/\$([A-Za-z0-9_]*)\$/);
    if (dollarMatch) {
      if (!inDollarQuote) {
        inDollarQuote = true;
        dollarTag = dollarMatch[0];
      } else if (line.includes(dollarTag)) {
        inDollarQuote = false;
        dollarTag = '';
      }
    }

    current += line + '\n';

    if (!inDollarQuote && trimmed.endsWith(';')) {
      const stmt = current.trim();
      if (stmt && stmt !== ';') {
        statements.push(stmt);
      }
      current = '';
    }
  }

  if (current.trim()) {
    statements.push(current.trim());
  }

  return statements;
}

async function run() {
  console.log('--- Migrating to Neon Cloud Database ---');
  
  const sqlFile = path.join(__dirname, 'supabase_full_migration.sql');
  const sqlContent = fs.readFileSync(sqlFile, 'utf8');

  const statements = splitStatements(sqlContent);
  console.log(`Found ${statements.length} SQL statements to execute.`);

  for (let i = 0; i < statements.length; i++) {
    const stmt = statements[i];
    const preview = stmt.replace(/\s+/g, ' ').slice(0, 60);
    // Skip START TRANSACTION and COMMIT since each HTTP query is auto-committed
    if (preview.toUpperCase().startsWith('START TRANSACTION') || preview.toUpperCase().startsWith('COMMIT')) {
      continue;
    }
    process.stdout.write(`[${i + 1}/${statements.length}] ${preview}... `);
    try {
      await executeSql(stmt);
      console.log('OK');
    } catch (err) {
      console.log('FAIL');
      console.error(err.message);
    }
  }

  // Verify tables
  console.log('\n--- Verifying created tables in Neon ---');
  const tables = await executeSql("SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name;");
  console.log('Tables:');
  console.log(tables.rows.map(r => r.table_name));

  // Verify categories
  const categories = await executeSql('SELECT count(*) as count FROM "Categories";');
  console.log('Categories count in Neon:', categories.rows[0].count);
}

run().catch(err => {
  console.error('Migration failed:', err);
  process.exit(1);
});
