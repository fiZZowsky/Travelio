// Natural Earth v5.1.2, public domain. Run: node tools/build-globe.cjs
const fs = require('node:fs');
const source = JSON.parse(fs.readFileSync('src/Travelio.UI/wwwroot/data/countries-50m-source.geojson', 'utf8'));
const round = value => Array.isArray(value) ? value.map(round) : Math.round(value * 10000) / 10000;
const features = source.features.map(f => ({type:'Feature', properties:{
    code: f.properties.ISO_A2_EH !== '-99' ? f.properties.ISO_A2_EH : f.properties.ISO_A2,
    name: f.properties.NAME_PL || f.properties.NAME,
    center: [f.properties.LABEL_X, f.properties.LABEL_Y],
    tiny: ['VA','MC','SM','LI','AD','MT','SG','BH','MV','NR','TV','MH','FM','PW','KN','LC','VC','GD','BB','AG'].includes(f.properties.ISO_A2_EH)
}, geometry:{type:f.geometry.type, coordinates:round(f.geometry.coordinates)}}));
fs.writeFileSync('src/Travelio.UI/wwwroot/data/countries.json', JSON.stringify({type:'FeatureCollection',features}));
console.log(JSON.stringify({countries:features.length,bytes:fs.statSync('src/Travelio.UI/wwwroot/data/countries.json').size}));
