import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import {
  permissionKeyOf,
  refusalForAction,
  refusalForMethod,
  refusalForModuleName,
  refusalForPascalName,
  refusalForPermissionKey,
  refusalForRoute,
  toConstantCase,
  toKebabCase,
  toPascalCase,
  toWords,
} from '../lib/names.mjs';

const NO_MODULES = { existingModules: [], removedModules: [] };

describe('name conversion', () => {
  it('converts PascalCase to kebab-case and CONSTANT_CASE, keeping acronyms together', () => {
    assert.equal(toKebabCase('ServiceRequests'), 'service-requests');
    assert.equal(toKebabCase('Demo'), 'demo');
    assert.equal(toKebabCase('HttpAPIClient2'), 'http-api-client2');
    assert.equal(toConstantCase('GetWidget'), 'GET_WIDGET');
  });

  it('converts kebab-case to PascalCase and words, and round-trips', () => {
    assert.equal(toPascalCase('approve-request'), 'ApproveRequest');
    assert.equal(toPascalCase('view'), 'View');
    assert.equal(toWords('approve-request'), 'approve request');
    assert.equal(toKebabCase(toPascalCase('approve-request')), 'approve-request');
  });

  it('builds the permission key from the module and the action', () => {
    assert.equal(permissionKeyOf('ServiceRequests', 'approve'), 'service-requests.approve');
  });
});

describe('name validation', () => {
  it('accepts PascalCase names', () => {
    assert.equal(refusalForPascalName('Records', 'name'), null);
    assert.equal(refusalForPascalName('ServiceRequests2', 'name'), null);
  });

  for (const bad of ['', 'records', 'Service_Requests', 'Service-Requests', '9Lives', 'ABC', 'A'.repeat(30), undefined]) {
    it(`refuses the non-Pascal name ${JSON.stringify(bad)}`, () => {
      assert.match(refusalForPascalName(bad, 'The name'), /The name/);
    });
  }

  it('refuses kernel and reserved names, case-insensitively', () => {
    assert.match(refusalForModuleName('Api', NO_MODULES), /kernel project/);
    assert.match(refusalForModuleName('Infrastructure', NO_MODULES), /kernel project/);
    assert.match(refusalForModuleName('Shared', NO_MODULES), /reserved/);
    assert.match(refusalForModuleName('Starter', NO_MODULES), /reserved/);
  });

  it('refuses an existing module and a name that only differs in case or kebab form', () => {
    assert.match(refusalForModuleName('Access', { existingModules: ['Access'], removedModules: [] }), /already exists/);
    assert.match(refusalForModuleName('Records', { existingModules: ['RecordS'], removedModules: [] }), /already exists/);
  });

  it('refuses a name listed in removedModules', () => {
    assert.match(refusalForModuleName('Demo', { existingModules: [], removedModules: ['Demo'] }), /removedModules/);
  });

  it('accepts a fresh business name', () => {
    assert.equal(refusalForModuleName('Records', { existingModules: ['Access', 'Identity'], removedModules: ['Demo'] }), null);
  });

  it('checks permission actions, keys, methods and routes', () => {
    assert.equal(refusalForAction('approve-request'), null);
    assert.match(refusalForAction('Approve'), /kebab-case/);
    assert.match(refusalForAction(''), /required/);
    assert.equal(refusalForPermissionKey('demo.approve-request'), null);
    assert.match(refusalForPermissionKey('demo'), /format/);
    assert.match(refusalForPermissionKey(`demo.${'a'.repeat(130)}`), /longer/);
    assert.equal(refusalForMethod('get'), null);
    assert.match(refusalForMethod('PATCH'), /not one of/);
    assert.equal(refusalForRoute('widgets/archive'), null);
    for (const bad of ['/widgets', 'widgets/', 'Widgets', 'api/widgets/{id}', '']) assert.ok(refusalForRoute(bad), bad);
  });
});
