const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const { test } = require('node:test');

const script = path.join(__dirname, 'manage-hack-users.sh');
const tenant = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const subscription = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const group = 'cccccccc-cccc-cccc-cccc-cccccccccccc';

function fixture(t) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'hack-users-test-'));
  t.after(() => fs.rmSync(dir, { recursive: true }));
  const stateFile = path.join(dir, 'state.json');
  const csv = path.join(dir, 'users.csv');
  fs.writeFileSync(stateFile, JSON.stringify({
    tenant, subscription, users: [], events: [], groups: {},
  }));
  const az = path.join(dir, 'az');
  fs.writeFileSync(az, `#!${process.execPath}
const fs = require('node:fs');
const args = process.argv.slice(2);
const file = process.env.FAKE_AZ_STATE;
const state = JSON.parse(fs.readFileSync(file, 'utf8'));
const option = name => args[args.indexOf(name) + 1];
const command = args.slice(0, 3).join(' ');
const emit = value => process.stdout.write(JSON.stringify(value));
const fail = message => { console.error(message); process.exit(1); };
state.events.push({ command, args: args.filter(a => !a.startsWith('@')) });
fs.writeFileSync(file, JSON.stringify(state));
if (args[0] === 'account') {
  emit({ tenantId: state.tenant, id: state.subscription });
} else if (command === 'ad group show') {
  emit({ securityEnabled: true, groupTypes: state.dynamic ? ['DynamicMembership'] : [] });
} else if (command === 'ad group list') {
  if (state.failGroupRead) fail('Group read access denied');
  const name = state.groupName || 'Hackathon MFA Exclusion';
  const expected = "displayName eq '" + name.replaceAll("'", "''") + "'";
  if (option('--filter') !== expected) fail('Incorrect exact-name OData filter');
  emit(state.namedGroups || [{ id: '${group}', displayName: name }]);
} else if (args[0] === 'rest' && option('--method') === 'get') {
  if (option('--url') !== 'https://graph.microsoft.com/v1.0/domains') fail('Unexpected Graph read');
  if (state.failDomainRead) fail('Domain read forbidden');
  emit({ value: [{ id: 'test.onmicrosoft.com', isVerified: true, authenticationType: 'Managed' }] });
} else if (command === 'ad user list') {
  const filter = option('--filter');
  emit(state.users.filter(u => filter === "id eq '" + u.id + "'" ||
    filter === "userPrincipalName eq '" + u.userPrincipalName + "'"));
} else if (args[0] === 'rest' && option('--method') === 'post') {
  const body = JSON.parse(fs.readFileSync(option('--body').slice(1), 'utf8'));
  if (option('--url') !== 'https://graph.microsoft.com/v1.0/users') fail('Unexpected Graph write');
  if (!body.accountEnabled || !body.passwordProfile.password.match(/^([A-Z][a-z]{2,5}-){4}[0-9]{6}$/)) {
    fail('Bad user request');
  }
  if (state.failCreate) fail('Creation denied; SECRET MUST NOT BE ECHOED');
  const id = 'dddddddd-dddd-dddd-dddd-' + String(state.users.length + 1).padStart(12, '0');
  const user = { id, userPrincipalName: body.userPrincipalName, body };
  state.users.push(user);
  emit(user);
} else if (command === 'ad group member') {
  if (state.failMembership) fail('Membership denied; SECRET MUST NOT BE ECHOED');
} else if (command === 'ad user delete') {
  state.users = state.users.filter(u => u.id !== option('--id'));
} else if (args[0] === 'group' && args[1] === 'exists') {
  emit(Boolean(state.groups[option('--name')]));
} else if (args[0] === 'group' && args[1] === 'show') {
  emit(state.groups[option('--name')]);
} else if (args[0] === 'group' && args[1] === 'delete') {
  if (option('--subscription') !== state.subscription) fail('Wrong subscription');
  delete state.groups[option('--name')];
} else {
  fail('Unexpected mock command: ' + args.join(' '));
}
fs.writeFileSync(file, JSON.stringify(state));
`, { mode: 0o700 });
  const read = () => JSON.parse(fs.readFileSync(stateFile, 'utf8'));
  const update = changes => fs.writeFileSync(stateFile, JSON.stringify({ ...read(), ...changes }));
  const run = args => spawnSync('bash', [script, ...args], {
    encoding: 'utf8',
    env: { ...process.env, PATH: `${dir}:${process.env.PATH}`, FAKE_AZ_STATE: stateFile },
  });
  const create = (extra = [], selector = ['--group-id', group]) => run([
    'create', '--count', '2', '--domain', 'test.onmicrosoft.com',
    ...selector, '--csv', csv, ...extra,
  ]);
  const remove = (extra = []) => run(['delete', '--csv', csv, ...extra]);
  const rows = () => fs.readFileSync(csv, 'utf8').trimEnd().split('\n').map(r => r.split(','));
  return { csv, read, update, run, create, remove, rows };
}

test('resolves exact group name, including apostrophes, to membership object ID', t => {
  const f = fixture(t);
  const name = "Hackathon's MFA Exclusion";
  f.update({ groupName: name });
  const result = f.create([], ['--group-name', name]);
  assert.equal(result.status, 0, result.stderr);
  const membership = f.read().events.filter(e => e.command === 'ad group member');
  assert.equal(membership.length, 2);
  for (const event of membership) {
    assert.equal(event.args[event.args.indexOf('--group') + 1], group);
  }
});

test('rejects missing, prefix-only and ambiguous group names without creating accounts', t => {
  const f = fixture(t);
  for (const namedGroups of [
    [],
    [{ id: group, displayName: 'Hackathon MFA Exclusion Extra' }],
    [{ id: group, displayName: 'Hackathon MFA Exclusion' },
      { id: tenant, displayName: 'Hackathon MFA Exclusion' }],
  ]) {
    f.update({ namedGroups });
    assert.notEqual(f.create(['--dry-run'], ['--group-name', 'Hackathon MFA Exclusion']).status, 0);
    assert.equal(f.read().users.length, 0);
    assert.ok(!fs.existsSync(f.csv));
  }
});

test('dry run performs read-only preflight without creating output directories or CSV', t => {
  const f = fixture(t);
  const output = path.join(path.dirname(f.csv), 'not-created', 'users.csv');
  const result = f.create(['--dry-run', '--csv', output], ['--group-name', 'Hackathon MFA Exclusion']);
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stderr, /DRY RUN/);
  assert.match(result.stderr, /Write permissions.*not verified/);
  assert.match(result.stderr, /Would create hack-user-02@test.onmicrosoft.com/);
  assert.ok(!fs.existsSync(path.dirname(output)));
  assert.equal(f.read().users.length, 0);
  assert.ok(f.read().events.every(e => ['account show --output', 'ad group list',
    'ad group show', 'rest --method get', 'ad user list'].includes(e.command)));
});

test('dry run fails on read access errors, unsupported groups and username collisions', t => {
  const f = fixture(t);
  f.update({ failGroupRead: true });
  assert.notEqual(f.create(['--dry-run'], ['--group-name', 'Hackathon MFA Exclusion']).status, 0);
  f.update({ failGroupRead: false, dynamic: true });
  assert.notEqual(f.create(['--dry-run']).status, 0);
  f.update({ dynamic: false, users: [{ userPrincipalName: 'hack-user-01@test.onmicrosoft.com' }] });
  assert.notEqual(f.create(['--dry-run']).status, 0);
  assert.ok(!fs.existsSync(f.csv));
  assert.ok(!f.read().events.some(e => e.command === 'rest --method post'));
});

test('rejects conflicting group selectors before accessing Azure', t => {
  const f = fixture(t);
  assert.notEqual(f.create(['--group-name', 'Hackathon MFA Exclusion']).status, 0);
  assert.equal(f.read().events.length, 0);
});

test('delete dry-run previews users without confirmation, cloud writes or CSV changes', t => {
  const f = fixture(t);
  assert.equal(f.create().status, 0);
  const csv = fs.readFileSync(f.csv, 'utf8');
  const before = f.read().events.length;
  for (const extra of [[], ['--yes']]) {
    const result = f.remove(['--dry-run', ...extra]);
    assert.equal(result.status, 0, result.stderr);
    assert.match(result.stderr, /Would delete user hack-user-01/);
    assert.match(result.stderr, /Would delete user hack-user-02/);
    assert.match(result.stderr, /not delete permissions/);
    for (const row of f.rows().slice(1)) assert.ok(!result.stderr.includes(row[1]));
  }
  assert.equal(f.read().users.length, 2);
  assert.equal(fs.readFileSync(f.csv, 'utf8'), csv);
  assert.ok(f.read().events.slice(before).every(e =>
    e.command === 'account show --output' || e.command === 'ad user list'));
});

test('delete dry-run validates tagged resource groups and skips missing users/groups', t => {
  const f = fixture(t);
  assert.equal(f.create().status, 0);
  const first = f.read().users[0];
  f.update({ users: [first], groups: {
    'rg-hack-user-01': { tags: {
      hackuser: first.userPrincipalName, environment: 'hack', createdBy: 'automation',
    } },
  } });
  const before = f.read().events.length;
  const result = f.remove(['--dry-run', '--delete-resource-groups']);
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stderr, /Would delete resource group rg-hack-user-01/);
  assert.match(result.stderr, /ALL resources/);
  assert.match(result.stderr, /User already absent: hack-user-02/);
  assert.match(result.stderr, /Resource group already absent: rg-hack-user-02/);
  assert.equal(f.read().users.length, 1);
  assert.ok(f.read().groups['rg-hack-user-01']);
  const events = f.read().events.slice(before);
  assert.ok(events.every(e => ['account show --output', 'ad user list',
    'group exists --name', 'group show --name'].includes(e.command)));
  for (const event of events.filter(e => e.command.startsWith('group '))) {
    assert.equal(event.args[event.args.indexOf('--subscription') + 1], subscription);
  }
});

test('delete dry-run rejects tenant, identity, ownership and malformed manifest mismatches', t => {
  const f = fixture(t);
  assert.equal(f.create().status, 0);
  const original = fs.readFileSync(f.csv, 'utf8');
  f.update({ tenant: group });
  assert.notEqual(f.remove(['--dry-run']).status, 0);
  f.update({ tenant });
  const users = f.read().users;
  const username = users[0].userPrincipalName;
  users[0].userPrincipalName = 'other@test.onmicrosoft.com';
  f.update({ users });
  assert.notEqual(f.remove(['--dry-run']).status, 0);
  users[0].userPrincipalName = username;
  f.update({ users, groups: { 'rg-hack-user-01': { tags: { environment: 'production' } } } });
  assert.notEqual(f.remove(['--dry-run', '--delete-resource-groups']).status, 0);
  fs.appendFileSync(f.csv, 'malformed,row\n');
  assert.notEqual(f.remove(['--dry-run']).status, 0);
  assert.equal(f.read().users.length, 2);
  assert.ok(!f.read().events.some(e => e.command === 'ad user delete' || e.command.startsWith('group delete')));
  fs.writeFileSync(f.csv, original);
});

test('creates bulk users with memorable random passwords matching the private CSV', t => {
  const f = fixture(t);
  const result = f.create();
  assert.equal(result.status, 0, result.stderr);
  const rows = f.rows();
  assert.equal(rows.length, 3);
  assert.equal(rows[0].join(','), 'username,password,resource_group,user_id,tenant_id,subscription_id');
  for (let i = 1; i <= 2; i++) {
    assert.equal(rows[i][0], `hack-user-${String(i).padStart(2, '0')}@test.onmicrosoft.com`);
    assert.equal(f.read().users[i - 1].body.displayName, `hack-user-${String(i).padStart(2, '0')}`);
    assert.equal(f.read().users[i - 1].body.mailNickname, f.read().users[i - 1].body.displayName);
    assert.match(rows[i][1], /^([A-Z][a-z]{2,5}-){4}[0-9]{6}$/);
    const password = f.read().users[i - 1].body.passwordProfile.password;
    assert.equal(rows[i][1], password);
    assert.ok(!`${result.stdout}${result.stderr}`.includes(password));
    assert.equal(rows[i][2], `rg-hack-user-${String(i).padStart(2, '0')}`);
    assert.equal(rows[i][4], tenant);
    assert.equal(rows[i][5], subscription);
    assert.equal(rows[i].length, 6);
    assert.ok(!`${result.stdout}${result.stderr}`.includes(rows[i][1]));
    assert.equal(f.read().users[i - 1].body.passwordProfile.forceChangePasswordNextSignIn, false);
  }
  assert.notEqual(rows[1][1], rows[2][1]);
  assert.equal(fs.statSync(f.csv).mode & 0o777, 0o600);
  const events = f.read().events;
  assert.equal(events.filter(e => e.command === 'ad group member').length, 2);
  assert.ok(!events.some(e => e.command.startsWith('group ')));
  assert.ok(!JSON.stringify(events).includes(rows[1][1]));
});

test('supports prefix, numbering, resource naming and password-change opt-in', t => {
  const f = fixture(t);
  const result = f.create(['--prefix', 'team', '--start', '10', '--resource-group-prefix', 'rg-event',
    '--force-password-change']);
  assert.equal(result.status, 0, result.stderr);
  assert.equal(f.rows()[1][0], 'team-10@test.onmicrosoft.com');
  assert.equal(f.rows()[2][2], 'rg-event-team-11');
  assert.equal(f.read().users[0].body.passwordProfile.forceChangePasswordNextSignIn, true);
});

test('pads custom names across 09/10 and preserves numbers 100 and above', t => {
  const f = fixture(t);
  const result = f.create(['--prefix', 'team', '--start', '9']);
  assert.equal(result.status, 0, result.stderr);
  assert.equal(f.rows()[1][0], 'team-09@test.onmicrosoft.com');
  assert.equal(f.rows()[2][0], 'team-10@test.onmicrosoft.com');
  assert.equal(f.rows()[1][2], 'rg-team-09');
  const before = f.read().events.length;
  const preview = f.create(['--prefix', 'team', '--start', '99', '--dry-run',
    '--csv', path.join(path.dirname(f.csv), 'preview.csv')]);
  assert.equal(preview.status, 0, preview.stderr);
  assert.match(preview.stderr, /Would create team-99@test.onmicrosoft.com/);
  assert.match(preview.stderr, /Would create team-100@test.onmicrosoft.com/);
  const lookups = f.read().events.slice(before).filter(e => e.command === 'ad user list');
  assert.ok(lookups[1].args.includes("userPrincipalName eq 'team-100@test.onmicrosoft.com'"));
});

test('supports explicit password-change opt-out', t => {
  const f = fixture(t);
  const result = f.create(['--no-force-password-change']);
  assert.equal(result.status, 0, result.stderr);
  assert.equal(f.read().users[0].body.passwordProfile.forceChangePasswordNextSignIn, false);
});

test('cleanup accepts both legacy password formats', t => {
  const f = fixture(t);
  assert.equal(f.create().status, 0);
  const rows = f.rows();
  rows[0] = ['username', 'password', 'resource_group', 'user_id', 'tenant_id', 'subscription_id'];
  rows[1][1] = 'Aa1!' + 'a'.repeat(32);
  rows[2][1] = 'Ha7-ABCD-EFGH-JKLM';
  fs.writeFileSync(f.csv, rows.map(row => row.slice(0, 6).join(',')).join('\n') + '\n');
  const result = f.remove(['--yes']);
  assert.equal(result.status, 0, result.stderr);
  assert.equal(f.read().users.length, 0);
});

test('refuses collisions, overwrites, invalid counts and nonignored repository output', t => {
  const f = fixture(t);
  f.update({ users: [{ userPrincipalName: 'hack-user-02@test.onmicrosoft.com' }] });
  assert.notEqual(f.create().status, 0);
  assert.ok(!fs.existsSync(f.csv));
  f.update({ users: [] });
  assert.notEqual(f.create(['--count', '0']).status, 0);
  assert.notEqual(f.create(['--count', '10001']).status, 0);
  assert.notEqual(f.create(['--csv', path.join(__dirname, 'unsafe-users.csv')]).status, 0);
  assert.equal(f.create().status, 0);
  const before = fs.readFileSync(f.csv, 'utf8');
  assert.notEqual(f.create().status, 0);
  assert.equal(fs.readFileSync(f.csv, 'utf8'), before);
});

test('rejects dynamic groups before creating users', t => {
  const f = fixture(t);
  f.update({ dynamic: true });
  assert.notEqual(f.create().status, 0);
  assert.equal(f.read().users.length, 0);
});

test('journals user before membership failure and does not expose CLI error contents', t => {
  const f = fixture(t);
  f.update({ failMembership: true });
  const result = f.create();
  assert.notEqual(result.status, 0);
  assert.equal(f.rows().length, 2);
  assert.equal(f.read().users[0].id, f.rows()[1][3]);
  assert.equal(f.rows()[1][1], f.read().users[0].body.passwordProfile.password);
  assert.ok(!result.stderr.includes('SECRET MUST NOT BE ECHOED'));
  assert.equal(f.remove(['--yes']).status, 0);
});

test('user creation failure never leaks credential-bearing CLI diagnostics', t => {
  const f = fixture(t);
  f.update({ failCreate: true });
  const result = f.create();
  assert.notEqual(result.status, 0);
  assert.ok(!result.stderr.includes('SECRET MUST NOT BE ECHOED'));
  assert.equal(f.rows().length, 1);
  assert.equal(f.read().users.length, 0);
});

test('reports actionable domain read failures without any writes', t => {
  const f = fixture(t);
  f.update({ failDomainRead: true });
  const result = f.create(['--dry-run']);
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /Cannot read verified Entra domains/);
  assert.match(result.stderr, /Domain read forbidden/);
  assert.match(result.stderr, /Domain.Read.All/);
  assert.ok(!fs.existsSync(f.csv));
  assert.equal(f.read().users.length, 0);
  assert.ok(!f.read().events.some(e => e.command === 'rest --method post'));
});

test('rejects extra fields or invalid passwords in manifests before deleting', t => {
  const f = fixture(t);
  assert.equal(f.create().status, 0);
  const original = fs.readFileSync(f.csv, 'utf8');
  const rows = f.rows();
  rows[2][1] = 'invalid';
  fs.writeFileSync(f.csv, rows.map(r => r.join(',')).join('\n') + '\n');
  assert.notEqual(f.remove(['--yes']).status, 0);
  fs.writeFileSync(f.csv, original.trimEnd() + ',extra\n');
  assert.notEqual(f.remove(['--yes']).status, 0);
  assert.equal(f.read().users.length, 2);
});

test('requires confirmation, validates every row and tenant before deleting', t => {
  const f = fixture(t);
  assert.equal(f.create().status, 0);
  assert.notEqual(f.remove().status, 0);
  f.update({ tenant: 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee' });
  assert.notEqual(f.remove(['--yes']).status, 0);
  f.update({ tenant });
  fs.appendFileSync(f.csv, 'invalid,row\n');
  assert.notEqual(f.remove(['--yes']).status, 0);
  assert.equal(f.read().users.length, 2);
  assert.ok(!f.read().events.some(e => e.command === 'ad user delete'));
});

test('deletes only recorded user IDs, leaves RGs by default, supports rerunning cleanup', t => {
  const f = fixture(t);
  assert.equal(f.create().status, 0);
  const result = f.remove(['--yes']);
  assert.equal(result.status, 0, result.stderr);
  assert.equal(f.read().users.length, 0);
  assert.ok(!f.read().events.some(e => e.command.startsWith('group ')));
  assert.equal(f.remove(['--yes']).status, 0);
  assert.ok(fs.existsSync(f.csv));
});

test('refuses changed user identity and missing ownership tags', t => {
  const f = fixture(t);
  assert.equal(f.create().status, 0);
  const users = f.read().users;
  users[0].userPrincipalName = 'other@test.onmicrosoft.com';
  f.update({ users });
  assert.notEqual(f.remove(['--yes']).status, 0);
  users[0].userPrincipalName = f.rows()[1][0];
  f.update({ users, groups: { 'rg-hack-user-01': { tags: { environment: 'production' } } } });
  assert.notEqual(f.remove(['--yes', '--delete-resource-groups']).status, 0);
  assert.equal(f.read().users.length, 2);
  assert.ok(!f.read().events.some(e => e.command.startsWith('group delete')));
});

test('deletes tagged RGs in recorded subscription before users and skips absent RGs', t => {
  const f = fixture(t);
  assert.equal(f.create().status, 0);
  f.update({ groups: { 'rg-hack-user-01': {
    tags: { hackuser: f.rows()[1][0], environment: 'hack', createdBy: 'automation' },
  } } });
  const result = f.remove(['--yes', '--delete-resource-groups']);
  assert.equal(result.status, 0, result.stderr);
  assert.equal(f.read().users.length, 0);
  assert.deepEqual(f.read().groups, {});
  const events = f.read().events;
  const deletion = events.find(e => e.command.startsWith('group delete'));
  assert.equal(deletion.args[deletion.args.indexOf('--subscription') + 1], subscription);
  assert.ok(events.indexOf(deletion) < events.findIndex(e => e.command === 'ad user delete'));
});
