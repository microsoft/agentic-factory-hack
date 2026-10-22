const assert = require('node:assert/strict');
const { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync } = require('node:fs');
const { tmpdir } = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const { test } = require('node:test');

const script = path.join(__dirname, 'provision-hack-environment.sh');
const template = path.join(__dirname, '../challenge-0/infra/azuredeploy.json');
const first = '5cjb5c6wemt32';
const second = 'gxq25v7mhxsdw';

function runProvision({ names = [], exists = true, listError = false, invalidJson = false,
    existsError = false, suffix } = {}) {
    const directory = mkdtempSync(path.join(tmpdir(), 'hack-provision-test-'));
    try {
        const bin = path.join(directory, 'bin');
        mkdirSync(bin);
        const log = path.join(directory, 'calls.jsonl');
        writeFileSync(path.join(bin, 'az'), `#!${process.execPath}
const fs = require('node:fs');
const args = process.argv.slice(2);
fs.appendFileSync(process.env.CALL_LOG, JSON.stringify(args) + '\\n');
const start = (...prefix) => prefix.every((value, index) => args[index] === value);
const value = (flag) => args[args.indexOf(flag) + 1];
if (start('account', 'show')) {
    console.log(value('--query') === 'id' ? 'test-subscription' : 'Test subscription');
} else if (start('ad', 'user', 'show')) {
    console.log('11111111-2222-3333-4444-555555555555');
} else if (start('group', 'exists')) {
    if (process.env.EXISTS_ERROR === 'true') {
        console.error('AuthorizationFailed');
        process.exit(1);
    }
    console.log(process.env.GROUP_EXISTS);
} else if (start('resource', 'list')) {
    if (process.env.LIST_ERROR === 'true') {
        console.error('AuthorizationFailed');
        process.exit(1);
    }
    console.log(process.env.RESOURCE_NAMES);
} else if (start('group', 'create') || start('role', 'assignment', 'create')) {
    process.exit(0);
} else if (start('deployment', 'group', 'create')) {
    console.log(JSON.stringify({properties: {outputs: {}}}));
} else {
    console.error('Unexpected CLI call: ' + JSON.stringify(args));
    process.exit(2);
}
`, { mode: 0o755 });
        const args = [script, '-u', 'hack-user-03@example.com',
            '-g', 'rg-hack-user-03', '-l', 'francecentral'];
        if (suffix !== undefined) args.push('--resource-suffix', suffix);
        const result = spawnSync('bash', args, {
            cwd: directory,
            input: 'y\n',
            encoding: 'utf8',
            env: {
                ...process.env,
                PATH: `${bin}:${process.env.PATH}`,
                CALL_LOG: log,
                GROUP_EXISTS: String(exists),
                EXISTS_ERROR: String(existsError),
                LIST_ERROR: String(listError),
                RESOURCE_NAMES: invalidJson ? '{}' : JSON.stringify(names),
            },
        });
        const calls = (() => {
            try {
                return readFileSync(log, 'utf8').trim().split('\n').filter(Boolean).map(JSON.parse);
            } catch (error) {
                if (error.code === 'ENOENT') return [];
                throw error;
            }
        })();
        return { ...result, calls };
    } finally {
        rmSync(directory, { recursive: true, force: true });
    }
}

function deployment(result) {
    assert.equal(result.status, 0, result.stdout + result.stderr);
    const call = result.calls.find((args) => args.slice(0, 3).join(' ') === 'deployment group create');
    assert.ok(call, 'Expected infrastructure deployment');
    assert.equal(call[call.indexOf('--subscription') + 1], 'test-subscription');
    return call;
}

function assertNoWrites(result) {
    assert.notEqual(result.status, 0);
    assert.ok(result.calls.every((args) => !args.includes('create')));
}

test('template naming is independent of the deployment name and preserves capacities', () => {
    const data = JSON.parse(readFileSync(template, 'utf8'));
    assert.equal(data.parameters.resourceSuffix.defaultValue, '[uniqueString(resourceGroup().id)]');
    assert.equal(data.variables.suffix, "[parameters('resourceSuffix')]");
    const models = data.resources.filter((resource) =>
        resource.type === 'Microsoft.CognitiveServices/accounts/deployments');
    assert.deepEqual(models.map((resource) => [resource.properties.model.name, resource.sku.capacity]),
        [['gpt-5.4-mini', 25], ['gpt-5.4', 25], ['text-embedding-3-large', 10]]);
});

test('Foundry telemetry connection uses the full Application Insights connection string', () => {
    const data = JSON.parse(readFileSync(template, 'utf8'));
    const connections = data.resources.filter((resource) =>
        resource.type === 'Microsoft.CognitiveServices/accounts/projects/connections'
        && resource.properties.category === 'AppInsights');
    assert.equal(connections.length, 1);
    const connection = connections[0];
    assert.equal(connection.properties.authType, 'ApiKey');
    assert.equal(connection.properties.credentials.key,
        "[reference(resourceId('Microsoft.Insights/components', variables('applicationInsightsName')), '2020-02-02').ConnectionString]");
    assert.ok(connection.dependsOn.includes(
        "[resourceId('Microsoft.Insights/components', variables('applicationInsightsName'))]"));
});

test('new resource group uses stable template default', () => {
    const call = deployment(runProvision({ exists: false }));
    assert.ok(!call.some((value) => value.startsWith('resourceSuffix=')));
});

test('empty existing resource group uses stable template default', () => {
    const call = deployment(runProvision());
    assert.ok(!call.some((value) => value.startsWith('resourceSuffix=')));
});

test('retry reuses a single legacy suffix across multiple resources', () => {
    const call = deployment(runProvision({ names: [
        `msagthack-aifoundry-${first}`, `msagthack-cosmos-${first}`,
        `msagthackcr${first}`, 'unrelated-resource',
    ] }));
    assert.ok(call.includes(`resourceSuffix=${first}`));
});

test('partial deployments can be discovered from any remaining resource', () => {
    for (const name of [`msagthack-cosmos-${first}`, `msagthackcr${first}`,
        `msagthacksa${first}`, `msagthack-loganalytics-${first}`]) {
        assert.ok(deployment(runProvision({ names: [name] })).includes(`resourceSuffix=${first}`));
    }
});

test('multiple suffixes stop before any writes', () => {
    const result = runProvision({ names: [
        `msagthack-cosmos-${first}`, `msagthack-cosmos-${second}`,
    ] });
    assertNoWrites(result);
    assert.match(result.stdout, /Multiple environment suffixes/);
    assert.match(result.stdout, new RegExp(first));
    assert.match(result.stdout, new RegExp(second));
});

test('explicit suffix selects an existing environment without deleting others', () => {
    const result = runProvision({ names: [
        `msagthack-cosmos-${first}`, `msagthack-cosmos-${second}`,
    ], suffix: second });
    assert.ok(deployment(result).includes(`resourceSuffix=${second}`));
    assert.ok(result.calls.every((args) => !args.includes('delete')));
});

test('explicit suffix cannot silently create another environment alongside an existing one', () => {
    assertNoWrites(runProvision({ names: [`msagthack-cosmos-${first}`], suffix: second }));
});

test('discovery and existence-check failures stop without mutations', () => {
    for (const options of [{ listError: true }, { invalidJson: true }, { existsError: true }]) {
        assertNoWrites(runProvision(options));
    }
});

test('invalid suffix is rejected before any Azure calls', () => {
    for (const suffix of ['short', 'UPPERCASE12345', 'bad/suffix123']) {
        const result = runProvision({ suffix });
        assertNoWrites(result);
        assert.equal(result.calls.length, 0);
    }
});
