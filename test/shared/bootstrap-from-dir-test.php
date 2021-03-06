<?php namespace xp\test;

$test= require 'test.php';
$path= require 'path.php';
$base= require 'bootstrap-base.php';

exit($test->run(array_merge($base, [
  'directory' => function() use($path) {
    $this->assertEquals(
      [[$path->compose($this->dir, '__xp.php') => true], [$this->dir => true]],
      \xp\bootstrap([$this->dir], function() { return []; })
    );
  },

  'directory before current path' => function() use($path) {
    $this->assertEquals(
      [[$path->compose($this->dir, '__xp.php') => true], [$this->dir => true, $this->cwd => true]],
      \xp\bootstrap([$this->dir, $this->cwd], function() { return []; })
    );
  },

  'directory after current path' => function() use($path) {
    $this->assertEquals(
      [[$path->compose($this->dir, '__xp.php') => true], [$this->cwd => true, $this->dir => true]],
      \xp\bootstrap([$this->cwd, $this->dir], function() { return []; })
    );
  },

  'directory after merging' => function() use($path) {
    $this->assertEquals(
      [[$path->compose($this->dir, '__xp.php') => true], [$this->dir => true]],
      \xp\bootstrap([], function() { return [$this->dir]; })
    );
  },

  'directory after merging after current path' => function() use($path) {
    $this->assertEquals(
      [[$path->compose($this->dir, '__xp.php') => true], [$this->cwd => true, $this->dir => true]],
      \xp\bootstrap([$this->cwd], function() { return [$this->dir]; })
    );
  },

  'directory after merging before current path' => function() use($path) {
    $this->assertEquals(
      [[$path->compose($this->dir, '__xp.php') => true], [$this->dir => true, $this->cwd => true]],
      \xp\bootstrap([], function() { return [$this->dir, $this->cwd]; })
    );
  },
])));