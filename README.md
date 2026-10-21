# Setup of new plugin

## Checklist before deploy
- [ ] Rename namespaces from DATASOURCENAME to plugin name
- [ ] Ensure deploy pipeline points at correct project
- [ ] Ensure azure function is created in Azure (will be empty until first plugin deploy)
- [ ] Update function host keys per environment in Azure
- [ ] Update github with deploy variables and secrets
- [ ] Set up authentication in azure function to allow dancore to call plugin
- [ ] Add application settings in Azure
