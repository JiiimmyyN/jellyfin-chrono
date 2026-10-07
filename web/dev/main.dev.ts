import { installFakeHost } from './host';
import { installMockApi } from './mock';

installFakeHost();
installMockApi();
void import('../src/main');
